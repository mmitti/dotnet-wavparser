using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;

namespace NokitaKaze.WAVParser
{
    public class WAVParser
    {
        public const ushort WAVE_FORMAT_PCM = 0x0001;
        public const ushort WAVE_FORMAT_IEEE_FLOAT = 0x0003;
        public const ushort WAVE_FORMAT_EXTENSIBLE = 0xfffe;

        public List<List<double>> Samples;
        public int ChannelCount;
        public int SampleRate;
        public int BitsPerSample;
        public int BlockAlign;
        public long StartDataSeek { get; protected set; }
        public int AudioFormat;

        public WAVParser()
        {
            ChannelCount = 2;
            SampleRate = 44100;
            BitsPerSample = 16;
            BlockAlign = 2;
        }

        public WAVParser(Stream stream)
        {
            ParseStream(stream);
        }

        public WAVParser(IEnumerable<byte> bytes)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(bytes.ToArray(), 0, bytes.Count());
                ParseStream(ms);
            }
        }

        public int SamplesCount
        {
            get
            {
                if ((Samples == null) || !Samples.Any())
                {
                    return 0;
                }

                return Samples[0].Count;
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
                var t = TimeSpan.FromSeconds(this.SamplesCount * 1d / this.SampleRate);
                s += ", duration: " + t;
            }

            return s;
        }

        #region Time spans

        public TimeSpan GetSpanForSamples(long samplesCount)
        {
            return TimeSpan.FromTicks((long) Math.Round(10_000_000d * samplesCount / this.SampleRate));
        }

        public long GetFloorSamplesCount(TimeSpan span)
        {
            return (long) Math.Floor(span.TotalSeconds * this.SampleRate);
        }

        public long GetFloorSamplesCount(double seconds)
        {
            return GetFloorSamplesCount(TimeSpan.FromSeconds(seconds));
        }

        #endregion

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
                    throw new Exception("This is not a RIFF file");
                }

                riffSize = rd.ReadUInt32();
                var riffFormat = Encoding.ASCII.GetString(rd.ReadBytes(4));
                if (riffFormat != "WAVE")
                {
                    throw new Exception("This is not a WAVE file");
                }
            }

            bool formatFound = false;
            bool lastChunk = false;

            while (!lastChunk)
            {
                var currentSeek = stream.Position;
                var subchunkID = Encoding.ASCII.GetString(rd.ReadBytes(4));
                var subchunkSize = rd.ReadUInt32();
                lastChunk = (subchunkSize + stream.Position == startSeek + riffSize + 8);
                if (subchunkSize + stream.Position > startSeek + riffSize + 8)
                {
                    throw new Exception("Subchunk size is bigger than entire RIFF file");
                }

                switch (subchunkID)
                {
                    case "fmt ":
                        ReadHeader(rd);
                        formatFound = true;
                        break;
                    case "data":
                        if (!formatFound)
                        {
                            throw new Exception("Data chunk before format chunk");
                        }

                        StartDataSeek = currentSeek + 8;
                        ReadData(rd, stream, subchunkSize);

                        break;
                    // TODO: list info
                }

                stream.Seek(currentSeek + 8 + subchunkSize - stream.Position, SeekOrigin.Current);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rd"></param>
        /// <exception cref="Exception"></exception>
        /// https://docs.microsoft.com/ru-ru/previous-versions/dd757713(v=vs.85)
        protected void ReadHeader(BinaryReader rd)
        {
            this.AudioFormat = rd.ReadUInt16();
            if (
                (this.AudioFormat != WAVE_FORMAT_PCM) &&
                (this.AudioFormat != WAVE_FORMAT_IEEE_FLOAT)
            )
            {
                throw new Exception(string.Format(
                    "This is not a plain PCM file (unsupported wave format 0x{0:x4})", this.AudioFormat));
            }

            this.ChannelCount = rd.ReadUInt16();
            this.SampleRate = (int) rd.ReadUInt32();
            rd.ReadBytes(4);
            this.BlockAlign = rd.ReadUInt16();
            this.BitsPerSample = rd.ReadUInt16();

            // TODO waveformatextensible
        }

        protected void ReadData(BinaryReader rd, Stream stream, long chunkSize)
        {
            switch (this.AudioFormat)
            {
                case WAVE_FORMAT_PCM:
                    ReadData_PCM(rd, stream, chunkSize);
                    return;
                case WAVE_FORMAT_IEEE_FLOAT:
                    ReadData_IEEE_FLOAT(rd, stream, chunkSize);
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
        protected void ReadData_PCM(BinaryReader rd, Stream stream, long chunkSize)
        {
            const double bit8R_up = 1 / (1d * sbyte.MaxValue);
            const double bit8R_down = -1 / (1d * sbyte.MinValue);
            const double bit16R_up = 1 / (1d * short.MaxValue);
            const double bit16R_down = -1 / (1d * short.MinValue);
            const double bit32R_up = 1 / (1d * int.MaxValue);
            const double bit64R_up = 1 / (1d * long.MaxValue);

            var startPosition = stream.Position;
            // TODO: https://docs.microsoft.com/en-us/windows/win32/api/mmreg/ns-mmreg-waveformatextensible

            Samples = new List<List<double>>();
            var samplesCount = (int) (chunkSize / this.BlockAlign);
            for (int i = 0; i < this.ChannelCount; i++)
            {
                Samples.Add(new List<double>(samplesCount));
            }

            var additionalSeekSize = this.BlockAlign - this.ChannelCount * this.BitsPerSample / 8;
            while ((stream.Position - startPosition) <= chunkSize - this.BlockAlign)
            {
                for (int channel = 0; channel < this.ChannelCount; channel++)
                {
                    double value;

                    switch (this.BitsPerSample)
                    {
                        case 8:
                        {
                            var raw = (int) rd.ReadByte();
                            raw += sbyte.MinValue;

                            value = (raw > 0) ? raw * bit8R_up : raw * bit8R_down;
                            break;
                        }

                        case 16:
                        {
                            var raw = rd.ReadInt16();
                            value = (raw > 0) ? raw * bit16R_up : raw * bit16R_down;
                            break;
                        }

                        /*
                        case 24:
                        {
                            throw new NotImplementedException();
                            break;
                        }

                        case 32:
                        {
                            var raw = rd.ReadInt32();
                            value = raw * bit32R;
                            break;
                        }

                        case 64:
                        {
                            var raw = rd.ReadInt64();
                            value = raw * bit64R;
                            break;
                        }
                        */

                        default:
                            throw new Exception(string.Format(
                                "This Bit Per Sample ({0}) is not implemented", this.BitsPerSample));
                    }

                    Samples[channel].Add(value);
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
        /// <exception cref="Exception"></exception>
        /// https://en.wikipedia.org/wiki/IEEE_754
        protected void ReadData_IEEE_FLOAT(BinaryReader rd, Stream stream, long chunkSize)
        {
            var startPosition = stream.Position;
            Samples = new List<List<double>>();
            var samplesCount = (int) (chunkSize / this.BlockAlign);
            for (int i = 0; i < this.ChannelCount; i++)
            {
                Samples.Add(new List<double>(samplesCount));
            }

            var additionalSeekSize = this.BlockAlign - this.ChannelCount * this.BitsPerSample / 8;
            while ((stream.Position - startPosition) <= chunkSize - this.BlockAlign)
            {
                for (int channel = 0; channel < this.ChannelCount; channel++)
                {
                    double value;

                    switch (this.BitsPerSample)
                    {
                        case 32:
                        {
                            var raw = rd.ReadSingle();
                            value = Math.Min(Math.Max(raw, -1), 1);
                            break;
                        }

                        default:
                            throw new NotImplementedException(string.Format(
                                "This Bit Per Sample ({0}) is not implemented", this.BitsPerSample));
                    }

                    Samples[channel].Add(value);
                }

                stream.Seek(additionalSeekSize, SeekOrigin.Current);
            }
        }
    }
}