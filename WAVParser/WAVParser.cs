using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

        public List<List<double>> Samples;
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
        }

        static WAVParser()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var t = (System.Reflection.AssemblyFileVersionAttribute) assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyFileVersionAttribute))
                .First();

            ParserVersion = t.Version;
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
                ms.Seek(0, SeekOrigin.Begin);
                ParseStream(ms);
            }
        }

        public WAVParser(string filename)
        {
            using (var stream = File.Open(filename, FileMode.Open))
            {
                ParseStream(stream);
            }
        }

        #endregion

        #region Save

        public void Save(string filename)
        {
            using (var stream = File.Open(filename, FileMode.Create))
            {
                Save(stream);
            }
        }

        public void Save(Stream stream)
        {
            var data = this.GetDataAsRiff();
            stream.Write(data);
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

        protected const double bit8R_up = 1 / (1d * sbyte.MaxValue);
        protected const double bit8R_down = -1 / (1d * sbyte.MinValue);
        protected const double bit16R_up = 1 / (1d * short.MaxValue);
        protected const double bit16R_down = -1 / (1d * short.MinValue);
        protected const double bit24R_up = 1 / (1d * ((1 << 23) - 1));
        protected const double bit24R_down = -1 / (1d * (1 << 23));
        protected const double bit32R_up = 1 / (1d * int.MaxValue);
        protected const double bit32R_down = -1 / (1d * int.MinValue);
        protected const double bit64R_up = 1 / (1d * long.MaxValue);
        protected const double bit64R_down = -1 / (1d * long.MinValue);

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
                case WAVE_FORMAT_IEEE_FLOAT:
                    ReadData_IEEE_FLOAT(rd, stream, chunkSize);
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

                            value = (raw >= 0) ? raw * bit8R_up : raw * bit8R_down;
                            break;
                        }

                        case 16:
                        {
                            var raw = rd.ReadInt16();
                            value = (raw >= 0) ? raw * bit16R_up : raw * bit16R_down;
                            break;
                        }

                        case 24:
                        {
                            var bytes = rd.ReadBytes(3).ToList();
                            bytes.Add(0);
                            var rawUint32 = BitConverter.ToUInt32(bytes.ToArray(), 0);

                            if ((rawUint32 & 0b1000_0000_0000_0000_0000_0000) == 0b1000_0000_0000_0000_0000_0000)
                            {
                                value = -bit24R_down * rawUint32 - 2;
                            }
                            else
                            {
                                value = rawUint32 * bit24R_up;
                            }

                            break;
                        }

                        case 32:
                        {
                            var raw = rd.ReadInt32();
                            value = (raw >= 0) ? raw * bit32R_up : raw * bit32R_down;
                            break;
                        }

                        case 64:
                        {
                            var raw = rd.ReadInt64();
                            value = (raw >= 0) ? raw * bit64R_up : raw * bit64R_down;
                            break;
                        }

                        default:
                            throw new ParsingException(string.Format(
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

                        case 64:
                        {
                            var raw = rd.ReadDouble();
                            value = Math.Min(Math.Max(raw, -1), 1);
                            break;
                        }

                        default:
                            throw new ParsingException(string.Format(
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
        /// <exception cref="NotImplementedException"></exception>
        /// https://docs.microsoft.com/en-us/windows/win32/api/mmreg/ns-mmreg-waveformatextensible
        protected void ReadData_EXTENSIBLE(BinaryReader rd, Stream stream, long chunkSize)
        {
            switch (this.ExtensionAudioFormat)
            {
                case WAVE_FORMAT_PCM:
                    ReadData_PCM(rd, stream, chunkSize);
                    return;
                case WAVE_FORMAT_IEEE_FLOAT:
                    ReadData_IEEE_FLOAT(rd, stream, chunkSize);
                    return;
                default:
                    throw new NotImplementedException(string.Format("Not supported extension sub format 0x{0:x4}",
                        this.AudioFormat));
            }
        }

        #endregion

        #region Write Data

        protected const double bit8_up = 1d * sbyte.MaxValue;
        protected const double bit8_down = -1d * sbyte.MinValue;
        protected const double bit16_up = 1d * short.MaxValue;
        protected const double bit16_down = -1d * short.MinValue;
        protected const double bit32_up = 1d * int.MaxValue;
        protected const double bit32_down = -1d * int.MinValue;
        protected const double bit64_up = 1d * long.MaxValue;
        protected const double bit64_down = -1d * long.MinValue;

        public byte[] GetDataAsRiff()
        {
            using (var ms = new MemoryStream())
            {
                var formatChunk = GetFormatChunk();
                var infoChunk = GetListChunk();
                var rawPCMData = GetRawPCMData();

                var rw = new BinaryWriter(ms);
                rw.Write(Encoding.ASCII.GetBytes("RIFF"));
                rw.Write((uint) (formatChunk.Length + infoChunk.Length + rawPCMData.Length + 4));
                rw.Write(Encoding.ASCII.GetBytes("WAVE"));
                rw.Write(formatChunk);
                rw.Write(infoChunk);
                rw.Write(rawPCMData);

                ms.Seek(0, SeekOrigin.Begin);
                return ms.ToArray();
            }
        }

        protected static byte[] FormatChunk(string title, byte[] input)
        {
            if (title.Length != 4)
            {
                throw new Exception();
            }

            using (var ms = new MemoryStream())
            {
                var rw = new BinaryWriter(ms);

                rw.Write(Encoding.ASCII.GetBytes(title));
                rw.Write((uint) input.Length);
                rw.Write(input);

                ms.Seek(0, SeekOrigin.Begin);
                return ms.ToArray();
            }
        }

        protected byte[] GetFormatChunk()
        {
            using (var ms = new MemoryStream())
            {
                var averageBPS = this.SampleRate * this.ChannelCount * this.BitsPerSample / 8;

                var rw = new BinaryWriter(ms);
                rw.Write((ushort) 1); // TODO: correct this.AudioFormat
                rw.Write(this.ChannelCount);
                rw.Write(this.SampleRate);
                rw.Write(averageBPS);
                rw.Write((ushort) (this.BitsPerSample * this.ChannelCount / 8));
                rw.Write(this.BitsPerSample);

                ms.Seek(0, SeekOrigin.Begin);
                return FormatChunk("fmt ", ms.ToArray());
            }
        }

        protected byte[] GetListChunk()
        {
            using (var ms = new MemoryStream())
            {
                var rw = new BinaryWriter(ms);

                var ver = Encoding.ASCII.GetBytes(GetFullParserVersion()).ToList();
                if (ver.Count % 2 == 1)
                {
                    // Padding byte
                    ver.Add(0);
                }

                rw.Write(Encoding.ASCII.GetBytes("INFO"));
                rw.Write(Encoding.ASCII.GetBytes("ISFT"));
                rw.Write((uint) ver.Count);
                rw.Write(ver.ToArray());

                return FormatChunk("LIST", ms.ToArray());
            }
        }

        protected byte[] GetRawPCMData()
        {
            using (var ms = new MemoryStream())
            {
                var rw = new BinaryWriter(ms);

                var count = Samples[0].Count;

                for (int i = 0; i < count; i++)
                {
                    // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
                    foreach (var datum in Samples)
                    {
                        var value = datum[i];

                        switch (this.BitsPerSample)
                        {
                            case 8:
                            {
                                var r = (value >= 0) ? value * bit8_up : value * bit8_down;
                                r -= sbyte.MinValue;

                                rw.Write((byte) Math.Round(r));

                                break;
                            }

                            case 16:
                            {
                                short raw = (short) ((value >= 0) ? value * bit16_up : value * bit16_down);
                                rw.Write(raw);
                                break;
                            }

                            case 32:
                            {
                                int raw = (int) ((value >= 0) ? value * bit32_up : value * bit32_down);
                                rw.Write(raw);
                                break;
                            }

                            case 64:
                            {
                                // Hint: Not correct format for plain PCM with format = 0x0001
                                long raw = (long) ((value >= 0) ? value * bit64_up : value * bit64_down);
                                rw.Write(raw);
                                break;
                            }
                        }
                    }
                }

                return FormatChunk("data", ms.ToArray());
            }
        }

        #endregion
    }
}