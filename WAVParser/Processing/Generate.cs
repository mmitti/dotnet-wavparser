using System;
using System.Collections.Generic;

namespace NokitaKaze.WAVParser.Processing
{
    public static class Generate
    {
        public static void AddSilence(WAVParser input, TimeSpan duration)
        {
            var samplesCount = (int) input.GetFloorSamplesCount(duration);
            var buffer = new double[samplesCount];

            foreach (var channel in input.Samples)
            {
                channel.AddRange(buffer);
            }
        }
    }
}