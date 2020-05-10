using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NokitaKaze.WAVParser.Processing;
using Xunit;

namespace NokitaKaze.WAVParser.Test
{
    public class ProcessingTest
    {
        #region Init

        private static readonly string SourceCodeFolder;

        static ProcessingTest()
        {
            var current = Directory.GetCurrentDirectory();
            while (!Directory.Exists(current + "/WAVParser.Test") && (current.Length > 3))
            {
                current = Directory.GetParent(current).FullName;
            }

            if (!Directory.Exists(current + "/WAVParser.Test"))
            {
                throw new Exception("Wrong execution folder");
            }

            SourceCodeFolder = current;
        }

        private static string ResolveDataFile(string rawFilename)
        {
            var testFilenames = new[]
            {
                Directory.GetCurrentDirectory() + "\\" + rawFilename,
                SourceCodeFolder + "\\WAVParser.Test\\" + rawFilename,
                SourceCodeFolder + "\\" + rawFilename,
            };

            foreach (var testFilename in testFilenames)
            {
                if (File.Exists(testFilename))
                {
                    return testFilename;
                }
            }

            throw new Exception("Can not resolve file " + rawFilename);
        }

        #endregion

        #region Change Volume

        public static IEnumerable<object[]> ChangeVolumeTestData()
        {
            return new[]
            {
                // Audacity
                new object[] {"data/test1-s16le.wav", +3d, "data/test1-s16le-plus-3.wav", 0.005d},
                new object[] {"data/test1-s16le.wav", +6d, "data/test1-s16le-plus-6.wav", 0.005d},
                new object[] {"data/test1-s16le.wav", -3d, "data/test1-s16le-minus-3.wav", 0.005d},
                new object[] {"data/test1-s16le.wav", -6d, "data/test1-s16le-minus-6.wav", 0.005d},
                new object[] {"data/test1-s16le.wav", +15d, "data/test1-s16le-plus-15.wav", 0.01},
                new object[] {"data/test1-s16le.wav", -13d, "data/test1-s16le-minus-13.wav", 0.005d},

                // FFmpeg
                new object[] {"data/da_ni_spaceman-0.wav", +3d, "data/da_ni_spaceman-0-plus-3.wav", null},
                new object[] {"data/da_ni_spaceman-0.wav", +6d, "data/da_ni_spaceman-0-plus-6.wav", null},
                new object[] {"data/da_ni_spaceman-0.wav", +15d, "data/da_ni_spaceman-0-plus-15.wav", null},
                new object[] {"data/da_ni_spaceman-0.wav", +20d, "data/da_ni_spaceman-0-plus-20.wav", null},
                new object[] {"data/da_ni_spaceman-0.wav", -3d, "data/da_ni_spaceman-0-minus-3.wav", null},
                new object[] {"data/da_ni_spaceman-0.wav", -6d, "data/da_ni_spaceman-0-minus-6.wav", null},
                new object[] {"data/da_ni_spaceman-0.wav", -13d, "data/da_ni_spaceman-0-minus-13.wav", null},

                // FFmpeg
                new object[] {"data/da_ni_night-1-48000.wav", +3d, "data/da_ni_night-1-48000-plus-3.wav", null},
                new object[] {"data/da_ni_night-1-48000.wav", +6d, "data/da_ni_night-1-48000-plus-6.wav", null},
                new object[] {"data/da_ni_night-1-48000.wav", +15d, "data/da_ni_night-1-48000-plus-15.wav", null},
                new object[] {"data/da_ni_night-1-48000.wav", +20d, "data/da_ni_night-1-48000-plus-20.wav", null},
                new object[] {"data/da_ni_night-1-48000.wav", -3d, "data/da_ni_night-1-48000-minus-3.wav", null},
                new object[] {"data/da_ni_night-1-48000.wav", -6d, "data/da_ni_night-1-48000-minus-6.wav", null},
                new object[] {"data/da_ni_night-1-48000.wav", -13d, "data/da_ni_night-1-48000-minus-13.wav", null},
            };
        }

        [Theory]
        [MemberData(nameof(ChangeVolumeTestData))]
        public void ChangeVolumeTest(
            string input,
            double changeDb,
            string referenceFile,
            double? rmseMax
        )
        {
            WAVParser newFile;
            {
                var parser = new WAVParser(ResolveDataFile(input));
                newFile = parser.ChangeVolume(changeDb);
            }

            var parserReference = new WAVParser(ResolveDataFile(referenceFile));
            var rmseMaxValue = rmseMax ?? 0.000_01d;
            for (int channelId = 0; channelId < newFile.Samples.Count; channelId++)
            {
                var expected = parserReference.Samples[channelId];
                var actual = newFile.Samples[channelId];

                var sum = actual.Select((t, i) => Math.Pow(t - expected[i], 2)).Sum();

                var rmse = Math.Sqrt(sum / actual.Count);
                Assert.InRange(rmse, 0, rmseMaxValue);
            }
        }

        #endregion

        #region Merge Files

        public static IEnumerable<object[]> MergeFilesTestData()
        {
            var rawList = new List<object[]>
            {
                new object[]
                {
                    "data/chunk-1.wav", "data/chunk-2.wav",
                    "data/chunk-merge-0.wav",
                    MergeFileAlgorithm.Average, 0.000_011d,
                },
                new object[]
                {
                    "data/da_ni_spaceman-0.wav", "data/da_ni_night-0.wav",
                    "data/da_ni_spaceman-0__da_ni_night-0.wav",
                    MergeFileAlgorithm.Average, 0.000_011d,
                },
                new object[]
                {
                    "data/da_ni_spaceman-0.wav", "data/da_ni_night-1-48000.wav",
                    "data/da_ni_spaceman-0__da_ni_night-1.wav",
                    MergeFileAlgorithm.Average, 0.0015d,
                },
                new object[]
                {
                    "data/da_ni_spaceman-1.wav", "data/da_ni_night-0.wav",
                    "data/da_ni_spaceman-1__da_ni_night-0.wav",
                    MergeFileAlgorithm.Average, 0.000_011d,
                },
                new object[]
                {
                    "data/da_ni_spaceman-1.wav", "data/da_ni_night-1-48000.wav",
                    "data/da_ni_spaceman-1__da_ni_night-1.wav",
                    MergeFileAlgorithm.Average, 0.0015d,
                },
                new object[]
                {
                    "data/da_ni_spaceman-1-48000.wav", "data/da_ni_night-1-48000.wav",
                    "data/da_ni_spaceman-1__da_ni_night-1.1.wav",
                    MergeFileAlgorithm.Average, 0.000_011d,
                },
            };

            foreach (var inputFile in new[]
            {
                "data/da_ni_spaceman-0.wav",
                "data/da_ni_spaceman-1.wav",
                "data/da_ni_night-0.wav",
                "data/da_ni_night-1.wav",
                "data/da_ni_spaceman-1-48000.wav",
            })
            {
                rawList.Add(new object[]
                {
                    inputFile, "data/silence-20sec.wav",
                    inputFile,
                    MergeFileAlgorithm.AverageX2, 0.000_011d,
                });
                rawList.Add(new object[]
                {
                    inputFile, "data/silence-20sec.wav",
                    inputFile,
                    MergeFileAlgorithm.Sum, 0.000_011d,
                });
            }

            var fullList = rawList.ToList();
            fullList.AddRange(rawList.Select(item => new[] {item[1], item[0], item[2], item[3], item[4]}));

            return fullList;
        }

        [Theory]
        [MemberData(nameof(MergeFilesTestData))]
        public void MergeFilesTest(
            string filename1,
            string filename2,
            string referenceFilename,
            MergeFileAlgorithm? algorithm,
            double maxRMSE
        )
        {
            var algorithmReal = algorithm ?? MergeFileAlgorithm.AverageX2;

            var file1Parser = new WAVParser(ResolveDataFile(filename1));
            var file2Parser = new WAVParser(ResolveDataFile(filename2));
            var referenceFile = new WAVParser(ResolveDataFile(referenceFilename));
            var outputFile = file1Parser.MergeFile(file2Parser, algorithmReal);
            // outputFile.Save(@"H:\server\_svn\_open\WAVParser\WAVParser.Test\data\delme.wav");
            Assert.InRange((referenceFile.Duration - outputFile.Duration).TotalMilliseconds, -5, 5);
            if (outputFile.SampleRate != referenceFile.SampleRate)
            {
                outputFile = outputFile.ChangeSampleRate(referenceFile.SampleRate);
            }

            for (int channelId = 0; channelId < referenceFile.Samples.Count; channelId++)
            {
                var expected = referenceFile.Samples[channelId];
                var actual = outputFile.Samples[channelId];

                var count = Math.Min(actual.Count, expected.Count);
                double sum = 0;
                for (int i = 0; i < count; i++)
                {
                    sum += Math.Pow(actual[i] - expected[i], 2);
                }

                var rmse = Math.Sqrt(sum / actual.Count);
                Assert.InRange(rmse, 0, maxRMSE);
            }
        }

        #endregion

        #region Change samples rate

        public static IEnumerable<object[]> ChangeSampleRateData()
        {
            return new[]
            {
                new object[] {"data/da_ni_night-0.wav", "data/da_ni_night-0-48000.wav"},
                new object[] {"data/da_ni_night-1.wav", "data/da_ni_night-1-48000.wav"},
                new object[] {"data/da_ni_spaceman-0.wav", "data/da_ni_spaceman-0-48000.wav"},
                new object[] {"data/da_ni_spaceman-1.wav", "data/da_ni_spaceman-1-48000.wav"},
            };
        }

        [Theory]
        [MemberData(nameof(ChangeSampleRateData))]
        public void ChangeSampleRate(
            string inputFilename,
            string referenceFilename
        )
        {
            var inputFile = new WAVParser(ResolveDataFile(inputFilename));
            var referenceFile = new WAVParser(ResolveDataFile(referenceFilename));

            var outputFile = inputFile.ChangeSampleRate(referenceFile.SampleRate);
            Assert.Equal(referenceFile.SampleRate, outputFile.SampleRate);
            Assert.InRange((referenceFile.Duration - outputFile.Duration).TotalMilliseconds, -5, 5);

            for (int channelId = 0; channelId < referenceFile.Samples.Count; channelId++)
            {
                var expected = referenceFile.Samples[channelId];
                var actual = outputFile.Samples[channelId];

                var sum = actual.Select((t, i) => Math.Pow(t - expected[i], 2)).Sum();

                var rmse = Math.Sqrt(sum / actual.Count);
                Assert.InRange(rmse, 0, 0.0025d);
            }
        }

        #endregion
    }
}