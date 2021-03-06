using LibFLACSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace NokitaKaze.WAVParser
{
    /// <summary>
    /// 
    /// </summary>
    /// http://soundfile.sapp.org/doc/WaveFormat/
    /// http://www-mmsp.ece.mcgill.ca/Documents/AudioFormats/WAVE/WAVE.html
    public class WAVParser
    {
        public const ushort WAVE_FORMAT_PCM = 0x0001;
        public const ushort WAVE_FORMAT_IEEE_FLOAT = 0x0003;
        public const ushort WAVE_FORMAT_EXTENSIBLE = 0xfffe;

        public List<short[]> Samples;
        public ushort ChannelCount;
        public uint SampleRate;
        public ushort BitsPerSample;
        public ushort BlockAlign;
        public long StartDataSeek { get; protected set; }
        public ushort AudioFormat;
        public System.Guid ExtensionSubFormatGuid;

        public static readonly string ParserVersion;

        public static string GetFullParserVersion()
        {
            return "NokitaKaze-WAVParser-" + ParserVersion;
        }

        #region Load

        public WAVParser()
        {
            ChannelCount = 2;
            SampleRate = 44100;
            BitsPerSample = 16;
            BlockAlign = 4;
            AudioFormat = 1;
            Samples = new List<short[]>();
        }

        public WAVParser(string filename, bool readEntire = true, int sampleRate = -1)
        {
            if (System.IO.Path.GetExtension(filename) == ".flac")
            {
                ParseFlac(filename);
            }
            else
            {
                if (readEntire)
                {
                    // Significant increase in reading speed
                    var bytes = File.ReadAllBytes(filename);
                    using (var ms = new MemoryStream())
                    {
                        ms.Write(bytes.ToArray(), 0, bytes.Length);
                        ms.Seek(0, SeekOrigin.Begin);
                        ParseStream(ms);
                    }
                }
                else
                {
                    using (var stream = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        ParseStream(stream);
                    }
                }
            }
            if (sampleRate > 0)
            {
                Samples = Processing.ChangeSampleRate(Samples, (int)SampleRate, sampleRate);
                SampleRate = (uint)sampleRate;
            }
        }

        #endregion

        public int SamplesCount
        {
            get
            {
                if ((Samples == null) || !Samples.Any())
                {
                    return 0;
                }

                return Samples[0].Length;
            }
        }

        public override string ToString()
        {
            var s = string.Format(
                "{0}Hz {1} channels, {2} bits",
                this.SampleRate,
                this.ChannelCount,
                this.BitsPerSample
            );

            if (this.SamplesCount > 0)
            {
                const double TicksPerSecond = TimeSpan.TicksPerSecond * 1d;
                var t1 = this.SamplesCount * TicksPerSecond / this.SampleRate;
                var t = TimeSpan.FromTicks((long) t1);
                s += ", duration: " + t;
            }

            return s;
        }

        public ushort ExtensionAudioFormat => BitConverter.ToUInt16(this.ExtensionSubFormatGuid.ToByteArray(), 0);

        #region Time spans

        public TimeSpan GetSpanForSamples(long samplesCount)
        {
            const double TicksPerSecond = TimeSpan.TicksPerSecond * 1d;
            return TimeSpan.FromTicks((long) Math.Round(TicksPerSecond * samplesCount / this.SampleRate));
        }

        public long GetFloorSamplesCount(TimeSpan span)
        {
            return (long) Math.Floor(span.TotalSeconds * this.SampleRate);
        }

        public long GetFloorSamplesCount(double seconds)
        {
            return GetFloorSamplesCount(TimeSpan.FromSeconds(seconds));
        }

        public TimeSpan Duration => this.GetSpanForSamples(this.SamplesCount);

        #endregion

        #region Read Data
        private void ParseFlac(string path)
        {
            // reference
            // https://github.com/tgsstdio/BirdNest.Audio/blob/c1474db37be323355f6415cdd9011bc7f2550689/NAudioFLAC/Library/FLACFileReader.cs
            var context = IntPtr.Zero;
            try
            {
                context = LibFLAC.FLAC__stream_decoder_new();
                if (context == IntPtr.Zero) throw new Exception("FLAC open failed");
                int[] w_buf = null;
                int sample_index = 0;
                LibFLAC.StreamDecoderWriteStatus Write(IntPtr _, IntPtr frame_ptr, IntPtr buffer_ptr, IntPtr __)
                {
                    var frame = (LibFLAC.FlacFrame)Marshal.PtrToStructure(frame_ptr, typeof(LibFLAC.FlacFrame));
                    if(w_buf == null)
                    {
                        w_buf = new int[frame.Header.BlockSize * ChannelCount];
                    }
                    for (var c = 0; c < ChannelCount; ++c)
                    {
                        var buf = Marshal.ReadIntPtr(buffer_ptr, c * IntPtr.Size);
                        Marshal.Copy(buf, w_buf, c * frame.Header.BlockSize, frame.Header.BlockSize);
                    }
                    for (int i = 0; i < frame.Header.BlockSize; ++i)
                    {
                        for (int c = 0; c < ChannelCount; c++)
                        {
                            int sample = w_buf[i + c * frame.Header.BlockSize];

                            switch (BitsPerSample)
                            {
                                case 24:
                                    sample = sample >> 8;
                                    Samples[c][i + sample_index] = (short)sample;
                                    break;
                                case 16:
                                    Samples[c][i + sample_index] = (short)sample;
                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                    sample_index += frame.Header.BlockSize;
                    return LibFLAC.StreamDecoderWriteStatus.WriteStatusContinue;
                }
                void Metadata(IntPtr _, IntPtr metadata_ptr, IntPtr __)
                {
                    var metadata = (LibFLAC.FLACMetaData)Marshal.PtrToStructure(metadata_ptr, typeof(LibFLAC.FLACMetaData));
                    if (metadata.MetaDataType == LibFLAC.FLACMetaDataType.StreamInfo)
                    {
                        var stream_info_mem = GCHandle.Alloc(metadata.Data, GCHandleType.Pinned);
                        try
                        {
                            var stream_info = (LibFLAC.FLACStreamInfo)Marshal.PtrToStructure(stream_info_mem.AddrOfPinnedObject(), typeof(LibFLAC.FLACStreamInfo));
                            Samples = new List<short[]>();
                            SampleRate = (uint)stream_info.SampleRate;
                            var len = (long)(stream_info.TotalSamplesHi << 32) + stream_info.TotalSamplesLo;
                            for (int c = 0; c < stream_info.Channels; ++c)
                            {
                                Samples.Add(new short[len]);
                            }
                            ChannelCount = (ushort)stream_info.Channels;
                            BitsPerSample = (ushort)stream_info.BitsPerSample;
                            BlockAlign = 4;
                            AudioFormat = 1;
                        }
                        finally
                        {
                            stream_info_mem.Free();
                        }
                        if (BitsPerSample != 16 && BitsPerSample != 24)
                        {
                            throw new Exception("FLAC currently support 16/24sbit only.");
                        }
                    }
                }
                void Error(IntPtr _, LibFLAC.DecodeError status, IntPtr __)
                {
                    throw new Exception("FLAC decode error");
                }

                var status_code = LibFLAC.FLAC__stream_decoder_init_file(context, path, Write, Metadata, Error, IntPtr.Zero);
                if (status_code != 0)
                {
                    throw new Exception($"FLAC open error{status_code}");
                }
                var result = LibFLAC.FLAC__stream_decoder_process_until_end_of_stream(context);
                if (!result)
                {
                    throw new Exception($"FLAC decode process failed");
                }
            }
            finally
            {
                LibFLAC.FLAC__stream_decoder_finish(context);
                LibFLAC.FLAC__stream_decoder_delete(context);
            }
        }

        public static bool IsFlacAvailable()
        {
            IntPtr context = IntPtr.Zero;
            try
            {
                context = LibFLAC.FLAC__stream_decoder_new();
                return context != IntPtr.Zero;
            }
            catch {
                return false;
            }
            finally
            {
                if (context != IntPtr.Zero)
                {
                    LibFLAC.FLAC__stream_decoder_finish(context);
                    LibFLAC.FLAC__stream_decoder_delete(context);
                }
            }
        }

        protected void ParseStream(Stream stream)
        {
            Samples = null;
            var startSeek = stream.Position;
            var rd = new BinaryReader(stream);
            uint riffSize;
            {
                var riffMagic = Encoding.ASCII.GetString(rd.ReadBytes(4));
                if (riffMagic != "RIFF")
                {
                    throw new ParsingException("This is not a RIFF file");
                }

                riffSize = rd.ReadUInt32();
                var riffFormat = Encoding.ASCII.GetString(rd.ReadBytes(4));
                if (riffFormat != "WAVE")
                {
                    throw new ParsingException("This is not a WAVE file");
                }
            }

            bool formatFound = false;
            bool lastChunk = false;

            while (!lastChunk)
            {
                var currentSeek = stream.Position;
                var subChunkID = Encoding.ASCII.GetString(rd.ReadBytes(4));
                var subChunkSize = rd.ReadUInt32();
                lastChunk = (subChunkSize + stream.Position == startSeek + riffSize + 8);
                if (subChunkSize + stream.Position > startSeek + riffSize + 8)
                {
                    throw new ParsingException("Subchunk size is bigger than entire RIFF file");
                }

                switch (subChunkID)
                {
                    case "fmt ":
                        ReadHeader(rd, subChunkSize);
                        formatFound = true;
                        break;
                    case "data":
                        if (!formatFound)
                        {
                            throw new ParsingException("Data chunk before format chunk");
                        }

                        StartDataSeek = currentSeek + 8;
                        ReadData(rd, stream, subChunkSize);

                        break;
                    // TODO: list info
                }

                stream.Seek(currentSeek + 8 + subChunkSize - stream.Position, SeekOrigin.Current);
            }
        }

        /// <summary>
        /// Read fmt-header
        /// </summary>
        /// <param name="rd"></param>
        /// <param name="headerSize"></param>
        /// <exception cref="Exception"></exception>
        /// https://docs.microsoft.com/ru-ru/previous-versions/dd757713(v=vs.85)
        protected void ReadHeader(BinaryReader rd, long headerSize)
        {
            this.AudioFormat = rd.ReadUInt16();
            if (
                (this.AudioFormat != WAVE_FORMAT_PCM) &&
                (this.AudioFormat != WAVE_FORMAT_IEEE_FLOAT) &&
                (this.AudioFormat != WAVE_FORMAT_EXTENSIBLE)
            )
            {
                throw new ParsingException(string.Format(
                    "This is not a plain PCM file (unsupported wave format 0x{0:x4})", this.AudioFormat));
            }

            this.ChannelCount = rd.ReadUInt16();
            this.SampleRate = rd.ReadUInt32();
            rd.ReadBytes(4); // Average byte rate (byte per second / bps)
            this.BlockAlign = rd.ReadUInt16();
            this.BitsPerSample = rd.ReadUInt16();

            var headerLeft = headerSize - 4 * 4;
            if (headerLeft == 0)
            {
                if (this.AudioFormat == WAVE_FORMAT_EXTENSIBLE)
                {
                    throw new ParsingException("RIFF fmt-header doesn't contain additional header");
                }

                return;
            }

            var headerLeftSaid = rd.ReadUInt16();
            if (headerLeftSaid != headerLeft - 2)
            {
                throw new ParsingException(string.Format(
                    "RIFF fmt-header. Additional header malformed. Actual additional size {0} isn't equal to expected {1}",
                    headerLeft,
                    headerLeftSaid + 2
                ));
            }

            if (headerLeft == 2)
            {
                if (this.AudioFormat == WAVE_FORMAT_EXTENSIBLE)
                {
                    throw new ParsingException("RIFF fmt-header contain empty additional header");
                }

                return;
            }

            var samplesPerBlock = rd.ReadUInt16();

            if (samplesPerBlock != this.BitsPerSample)
            {
                throw new NotImplementedException("Extended WAVE real bit-per-sample isn't equal to previous one");
            }

            var channelMask = rd.ReadUInt32();
            {
                var guidBytes = rd.ReadBytes(16);
                ExtensionSubFormatGuid = new System.Guid(guidBytes);
            }
        }

        protected void ReadData(BinaryReader rd, Stream stream, long chunkSize)
        {
            switch (this.AudioFormat)
            {
                case WAVE_FORMAT_PCM:
                    ReadData_PCM(rd, stream, chunkSize);
                    return;
                case WAVE_FORMAT_EXTENSIBLE:
                    ReadData_EXTENSIBLE(rd, stream, chunkSize);
                    return;
                default:
                    throw new NotImplementedException(string.Format("Unsupported wave format 0x{0:x4}",
                        this.AudioFormat));
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rd"></param>
        /// <param name="stream"></param>
        /// <param name="chunkSize"></param>
        /// <exception cref="Exception"></exception>
        /// https://docs.microsoft.com/ru-ru/previous-versions/dd757713(v=vs.85)
        /// https://docs.microsoft.com/en-us/windows/win32/api/mmreg/ns-mmreg-waveformatex
        protected void ReadData_PCM(BinaryReader rd, Stream stream, long chunkSize)
        {
            var startPosition = stream.Position;
            var realChunkSize = Math.Min(stream.Length - startPosition, chunkSize);

            Samples = new List<short[]>();
            // hint: Здесь намеренно использован chunkSize, а не realChunkSize
            var samplesCount = (int) (chunkSize / this.BlockAlign);
            for (int i = 0; i < this.ChannelCount; i++)
            {
                Samples.Add(new short[samplesCount]);
            }

            var realSamplesCount = (int) (realChunkSize / this.BlockAlign);
            var additionalSeekSize = this.BlockAlign - this.ChannelCount * this.BitsPerSample / 8;
            switch (this.BitsPerSample)
            {
                case 8:
                {
                    ReadData_PCM_8bit(rd, stream, realSamplesCount, additionalSeekSize);
                    break;
                }
                case 16:
                {
                    ReadData_PCM_16bit(rd, stream, realSamplesCount, additionalSeekSize);
                    break;
                }
                case 24:
                {
                    ReadData_PCM_24bit(rd, stream, realSamplesCount, additionalSeekSize);
                    break;
                }
                default:
                    throw new ParsingException(string.Format(
                        "This Bit Per Sample ({0}) is not implemented", this.BitsPerSample));
            }
        }

        protected void ReadData_PCM_8bit(
            BinaryReader rd,
            Stream stream,
            int realSamplesCount,
            int additionalSeekSize
        )
        {
            for (int i = 0; i < realSamplesCount; i++)
            {
                for (int channel = 0; channel < this.ChannelCount; channel++)
                {
                    var raw = (int) rd.ReadByte();
                    raw += sbyte.MinValue;
                    Samples[channel][i] = (short)(raw * 256);
                }

                stream.Seek(additionalSeekSize, SeekOrigin.Current);
            }
        }

        protected void ReadData_PCM_16bit(
            BinaryReader rd,
            Stream stream,
            int realSamplesCount,
            int additionalSeekSize
        )
        {
            for (int i = 0; i < realSamplesCount; i++)
            {
                for (int channel = 0; channel < this.ChannelCount; channel++)
                {
                    var raw = rd.ReadInt16();
                    Samples[channel][i] = raw;
                }

                stream.Seek(additionalSeekSize, SeekOrigin.Current);
            }
        }

        protected void ReadData_PCM_24bit(
            BinaryReader rd,
            Stream stream,
            int realSamplesCount,
            int additionalSeekSize
        )
        {
            for (int i = 0; i < realSamplesCount; i++)
            {
                for (int channel = 0; channel < this.ChannelCount; channel++)
                {
                    var bytes = rd.ReadBytes(3);
                    Samples[channel][i] = (short)(bytes[1] | bytes[2] << 8);
                }

                stream.Seek(additionalSeekSize, SeekOrigin.Current);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rd"></param>
        /// <param name="stream"></param>
        /// <param name="chunkSize"></param>
        /// <exception cref="NotImplementedException"></exception>
        /// https://docs.microsoft.com/en-us/windows/win32/api/mmreg/ns-mmreg-waveformatextensible
        protected void ReadData_EXTENSIBLE(BinaryReader rd, Stream stream, long chunkSize)
        {
            switch (this.ExtensionAudioFormat)
            {
                case WAVE_FORMAT_PCM:
                    ReadData_PCM(rd, stream, chunkSize);
                    return;
                default:
                    throw new NotImplementedException(string.Format("Not supported extension sub format 0x{0:x4}",
                        this.AudioFormat));
            }
        }

        #endregion
    }
}