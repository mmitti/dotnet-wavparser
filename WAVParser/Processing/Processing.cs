using System;
using System.Collections.Generic;
using System.Linq;

namespace NokitaKaze.WAVParser.Processing
{
    public static class Processing
    {
        #region Change Volume

        public static List<double> ChangeVolume(IEnumerable<double> input, double changeDb)
        {
            var r = Math.Pow(10, changeDb / 20d);

            return input.Select(sample => Math.Max(-1, Math.Min(1, sample * r))).ToList();
        }

        public static WAVParser ChangeVolume(WAVParser wavParser, double changeDb)
        {
            var newSamples = wavParser
                .Samples
                .Select(channel => ChangeVolume(channel, changeDb))
                .ToList();

            var outputFile = wavParser.Clone();
            outputFile.Samples = newSamples;

            return outputFile;
        }

        #endregion

        #region Merge Files

        public static List<double> MergeSamplesAverage(
            IList<double> stream1,
            IList<double> stream2
        )
        {
            var maxId = Math.Min(stream1.Count, stream2.Count);
            var newStream = new List<double>();
            for (int i = 0; i < maxId; i++)
            {
                var v1 = stream1[i];
                var v2 = stream2[i];
                newStream.Add((v1 + v2) * 0.5d);
            }

            if (stream1.Count > stream2.Count)
            {
                for (int i = maxId; i < stream1.Count; i++)
                {
                    newStream.Add(stream1[i] * 0.5d);
                }
            }
            else if (stream1.Count < stream2.Count)
            {
                for (int i = maxId; i < stream2.Count; i++)
                {
                    newStream.Add(stream2[i] * 0.5d);
                }
            }

            return newStream;
        }

        public static List<double> MergeSamplesSum(
            IList<double> stream1,
            IList<double> stream2
        )
        {
            var maxId = Math.Min(stream1.Count, stream2.Count);
            var newStream = new List<double>();
            for (int i = 0; i < maxId; i++)
            {
                var v1 = stream1[i];
                var v2 = stream2[i];

                var value = v1 + v2;
                value -= Math.Sign(value) * Math.Abs(v1 * v2);
                newStream.Add(value);
            }

            if (stream1.Count > stream2.Count)
            {
                for (int i = maxId; i < stream1.Count; i++)
                {
                    newStream.Add(stream1[i]);
                }
            }
            else if (stream1.Count < stream2.Count)
            {
                for (int i = maxId; i < stream2.Count; i++)
                {
                    newStream.Add(stream2[i]);
                }
            }

            return newStream;
        }

        public static List<double> MergeSamples(
            IList<double> stream1,
            IList<double> stream2,
            MergeFileAlgorithm algorithm = MergeFileAlgorithm.Average
        )
        {
            switch (algorithm)
            {
                case MergeFileAlgorithm.Average:
                    return MergeSamplesAverage(stream1, stream2);
                case MergeFileAlgorithm.AverageX2:
                {
                    var raw = MergeSamplesAverage(stream1, stream2);
                    return raw.Select(t => Math.Max(Math.Min(t * 2, 1), -1)).ToList();
                }
                case MergeFileAlgorithm.Sum:
                    return MergeSamplesSum(stream1, stream2);
                default:
                    throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null);
            }
        }

        public static WAVParser MergeFiles(
            WAVParser file1,
            WAVParser file2,
            MergeFileAlgorithm algorithm = MergeFileAlgorithm.Average
        )
        {
            var realFile1 = file1.Clone();
            var realFile2 = file2.Clone();
            if (realFile1.SampleRate > realFile2.SampleRate)
            {
                realFile2 = ChangeSampleRate(realFile2, realFile1.SampleRate);
            }
            else if (realFile1.SampleRate < realFile2.SampleRate)
            {
                realFile1 = ChangeSampleRate(realFile1, realFile2.SampleRate);
            }

            var mergeMap = new List<List<int>>();

            if (realFile1.ChannelCount == realFile2.ChannelCount)
            {
                for (int i = 0; i < realFile1.ChannelCount; i++)
                {
                    mergeMap.Add(new List<int>() {i, i});
                }
            }
            else
            {
                if (realFile1.ChannelCount == 1)
                {
                    for (int i = 0; i < realFile2.ChannelCount; i++)
                    {
                        mergeMap.Add(new List<int>() {0, i});
                    }
                }
                else if (realFile2.ChannelCount == 1)
                {
                    for (int i = 0; i < realFile1.ChannelCount; i++)
                    {
                        mergeMap.Add(new List<int>() {i, 0});
                    }
                }
                else
                {
                    throw new Exception(string.Format("File 1 has {0} channels. File 2 has {1} channels. Can't merge",
                        realFile1.ChannelCount, realFile2.ChannelCount));
                }
            }

            var samples = new List<List<double>>();
            foreach (var map in mergeMap)
            {
                var channel1 = realFile1.Samples[map[0]];
                var channel2 = realFile2.Samples[map[1]];

                samples.Add(MergeSamples(channel1, channel2, algorithm));
            }

            var newFile = realFile1.Clone();
            newFile.Samples = samples;

            return newFile;
        }

        #endregion

        #region Change samples rate

        public static WAVParser ChangeSampleRate(WAVParser parser, uint newSampleRate)
        {
            return ChangeSampleRate(parser, (int) newSampleRate);
        }

        public static WAVParser ChangeSampleRate(WAVParser parser, int newSampleRate)
        {
            var newFile = parser.Clone(true);
            if (newFile.SampleRate == newSampleRate)
            {
                return newFile;
            }

            int hz1, hz2;
            {
                var nok = NOK((int) parser.SampleRate, newSampleRate);

                hz1 = (int) (nok / newSampleRate);
                hz2 = (int) (nok / parser.SampleRate);
            }

            int[] indexes;
            double[] rCoefs;
            {
                var indexesA = new List<int>(hz2);
                var rCoefsA = new List<double>(hz2);
                for (int j = 0; j < hz2; j++)
                {
                    var coef = j * 1d * hz1 / hz2;
                    var index1 = (int) Math.Floor(coef);
                    var r1 = coef - index1;

                    indexesA.Add(index1);
                    rCoefsA.Add(r1);
                }

                indexes = indexesA.ToArray();
                rCoefs = rCoefsA.ToArray();
            }

            var channelFull = new List<List<double>>();
            foreach (var channelA in parser.Samples.Select(t => t.ToList()))
            {
                int maxSampleCount = (int) Math.Ceiling(channelA.Count * 1d * hz2 / hz1);
                double[] channel;
                {
                    var fillValue = channelA.Last();
                    while (channelA.Count % hz1 != 0)
                    {
                        channelA.Add(fillValue);
                    }

                    channelA.Add(fillValue);
                    channel = channelA.ToArray();
                }

                var samplesNew = new List<double>();
                channelFull.Add(samplesNew);
                for (int i = 0; i < channel.Length - 1; i += hz1)
                {
                    samplesNew.Add(channel[i]);
                    for (int j = 1; j < hz2; j++)
                    {
                        var index1 = indexes[j];
                        var r1 = rCoefs[j];

                        var v1 = channel[index1 + i];
                        var v2 = channel[index1 + 1 + i];
                        if (v1 == v2)
                        {
                            samplesNew.Add(v1);
                            continue;
                        }

                        var value = (1 - r1) * v1 + r1 * v2;
                        samplesNew.Add(value);
                    }
                }

                while (samplesNew.Count > maxSampleCount)
                {
                    samplesNew.RemoveAt(samplesNew.Count - 1);
                }
            }

            newFile.Samples = channelFull;
            newFile.SampleRate = (ushort) newSampleRate;

            return newFile;
        }

        public static int NOD(int v1, int v2)
        {
            while (true)
            {
                if (v1 == v2)
                {
                    return v1;
                }

                if (v1 < v2)
                {
                    var t = v1;
                    v1 = v2;
                    v2 = t;
                }

                if (v1 % v2 == 0)
                {
                    return v2;
                }

                v1 %= v2;

                {
                    var t = v1;
                    v1 = v2;
                    v2 = t;
                }
            }
        }

        public static long NOK(int v1, int v2)
        {
            var nod = NOD(v1, v2);

            return ((long) v1 * v2 / nod);
        }

        #endregion
    }
}