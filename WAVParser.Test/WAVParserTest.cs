using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace NokitaKaze.WAVParser.Test
{
    public class WAVParserTest
    {
        public static IEnumerable<object[]> ParseFilesTest()
        {
            // ReSharper disable once UseObjectOrCollectionInitializer
            var data = new List<object[]>();
            // Pure PCM files with format = 0x0001 (WAVE_FORMAT_PCM)
            data.Add(new object[] {"./data/test1-u8.wav", 2, 48000, 8, 13536, null, null});
            data.Add(new object[] {"./data/test1-s16le.wav", 2, 48000, 16, 13536, "./data/test1-u8.wav", null});
            data.Add(new object[]
            {
                "./data/a441-16bit-square.wav", 1, 44100, 16, 400, null,
                new Tuple<int, double, bool>(441, 0.85d, true)
            });
            data.Add(new object[]
            {
                "./data/a441-16bit-square-1.wav", 1, 44100, 16, 400, null,
                new Tuple<int, double, bool>(441, 1d, true)
            });

            data.Add(new object[]
            {
                "./data/a441-16bit.wav", 1, 44100, 16, 44100, null,
                new Tuple<int, double, bool>(441, 0.85d, false)
            });

            // IEEE float (WAVE_FORMAT_IEEE_FLOAT)
            data.Add(new object[]
            {
                "./data/a441-32bit.float.wav", 1, 44100, 32, 44100,
                "./data/a441-16bit.wav",
                new Tuple<int, double, bool>(441, 0.85d, false)
            });

            // WAVE_FORMAT_EXTENSIBLE
            data.Add(new object[] {"./data/test1-s24le.wav", 2, 48000, 24, 13536, "./data/test1-u8.wav", null});
            data.Add(new object[] {"./data/test1-s32le.wav", 2, 48000, 32, 13536, "./data/test1-u8.wav", null});
            // data.Add(new object[] {"./data/test1-s64le.wav", 2, 48000, 64, 13536, "./data/test1-u8.wav", null});
            data.Add(new object[]
            {
                "./data/a441-24bit.exten.wav", 1, 44100, 24, 44100,
                "./data/a441-16bit.wav",
                new Tuple<int, double, bool>(441, 0.85d, false)
            });
            data.Add(new object[]
            {
                "./data/a441-32bit.exten.wav", 1, 44100, 32, 44100,
                "./data/a441-16bit.wav",
                new Tuple<int, double, bool>(441, 0.85d, false)
            });
            /*
            data.Add(new object[]
            {
                "./data/a441-64bit.exten-float.wav", 1, 44100, 64, 44100, 
                "./data/a441-16bit.wav",
                new Tuple<int, double, bool>(441, 0.85d, false)
            });
            */

            return data;
        }

        [Theory]
        [MemberData(nameof(ParseFilesTest))]
        public void ParseFiles(
            string filename,
            int channelCount,
            int sampleRate,
            int bitsPerSample,
            int sampleCount,
            string templateFilename,
            Tuple<int, double, bool> toneTest
        )
        {
            WAVParser parser;
            using (var stream = File.Open(filename, FileMode.Open))
            {
                parser = new WAVParser(stream);
                Assert.Equal(channelCount, parser.ChannelCount);
                Assert.Equal(sampleRate, parser.SampleRate);
                Assert.Equal(bitsPerSample, parser.BitsPerSample);

                Assert.NotNull(parser.ToString());
                Assert.InRange(parser.StartDataSeek, 42, 1024);

                Assert.Equal(sampleCount, parser.SamplesCount);
                foreach (var list in parser.Samples)
                {
                    Assert.Equal(sampleCount, list.Count);
                    foreach (var sample in list)
                    {
                        Assert.InRange(sample, -1, 1);
                    }
                }

                var durationDiff = parser.Duration - TimeSpan.FromSeconds(sampleCount * 1d / sampleRate);
                var durationDiffTicks = Math.Abs(durationDiff.Ticks);
                Assert.InRange(durationDiffTicks, 0, TimeSpan.TicksPerMillisecond - 1);

                Assert.Equal(TimeSpan.TicksPerSecond, parser.GetSpanForSamples(sampleRate).Ticks);
                Assert.Equal(sampleRate, parser.GetFloorSamplesCount(1));
            }

            if (templateFilename != null)
            {
                Assert.NotEqual(filename, templateFilename);
                WAVParser parserTemplate;
                using (var stream = File.Open(templateFilename, FileMode.Open))
                {
                    parserTemplate = new WAVParser(stream);
                }

                Assert.Equal(sampleCount, parserTemplate.SamplesCount);
                var minSampleRate = Math.Min(parserTemplate.BitsPerSample, parser.BitsPerSample);
                var minSampleRateC = 1 << (minSampleRate - 1);

                for (int channelId = 0; channelId < parser.Samples.Count; channelId++)
                {
                    var sum = 0d;
                    for (int i = 0; i < sampleCount; i++)
                    {
                        var expected = parserTemplate.Samples[channelId][i];
                        var real = parser.Samples[channelId][i];

                        var expected1 = Math.Round(expected * minSampleRateC) / minSampleRateC;
                        var real1 = Math.Round(real * minSampleRateC) / minSampleRateC;

                        sum += Math.Pow(real1 - expected1, 2);
                    }

                    var rmse = Math.Sqrt(sum / sampleCount);
                    Assert.InRange(rmse, 0, 0.006d);
                }
            }

            if (toneTest != null)
            {
                var (sinusoidHz, sinusoidValue, isSquare) = toneTest;
                var sinusoidHzR = Math.PI * 2 * sinusoidHz / parser.SampleRate;

                double minValue = double.NaN, maxValue = double.NaN;

                foreach (var channelSamples in parser.Samples)
                {
                    var sum = 0d;
                    var sumRMSE_Square = 0d;
                    var sumRMSE_Sin = 0d;

                    for (var i = 0; i < channelSamples.Count; i++)
                    {
                        var sample = channelSamples[i];

                        minValue = !double.IsNaN(minValue) ? Math.Min(sample, minValue) : sample;
                        maxValue = !double.IsNaN(maxValue) ? Math.Max(sample, maxValue) : sample;
                        sum += sample;
                        if (isSquare)
                        {
                            sumRMSE_Square += Math.Pow(Math.Abs(sample) - sinusoidValue, 2);
                        }
                        else
                        {
                            var angleRad = i * sinusoidHzR;
                            var expectedValue = Math.Sin(angleRad) * sinusoidValue;
                            sumRMSE_Sin += Math.Pow(sample - expectedValue, 2);
                        }
                    }

                    var averageValue = sum / channelSamples.Count;
                    Assert.InRange(averageValue, 0, 0.000_02d);
                    if (isSquare)
                    {
                        var rmse = Math.Sqrt(sumRMSE_Square / channelSamples.Count);
                        Assert.InRange(rmse, 0, 0.000_1d);
                    }
                    else
                    {
                        var rmse = Math.Sqrt(sumRMSE_Sin / channelSamples.Count);
                        Assert.InRange(rmse, 0, 0.000_1d);
                    }
                }

                {
                    var sinusoidValue_Min = Math.Min(sinusoidValue / 1.00037d, sinusoidValue);
                    var sinusoidValue_Max = Math.Min(sinusoidValue * 1.00037d, 1);

                    Assert.InRange(-minValue, sinusoidValue_Min, sinusoidValue_Max);
                    Assert.InRange(maxValue, sinusoidValue_Min, sinusoidValue_Max);
                }
            }
        }

        [Fact]
        public void CheckWorkingPureItem()
        {
            var item = new WAVParser();
            Assert.Equal(1, item.AudioFormat);
            Assert.InRange(item.ChannelCount, 1, int.MaxValue);
            Assert.Equal(0, item.BitsPerSample % 8);
            Assert.InRange(item.BitsPerSample, 1, 96_000);
            Assert.Equal((item.BitsPerSample / 8) * item.ChannelCount, item.BlockAlign);
            Assert.Equal(0, item.SamplesCount);
            Assert.Equal(TimeSpan.Zero, item.Duration);
            Assert.NotNull(item.ToString());
        }
    }
}