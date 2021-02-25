using System;
using System.Collections.Generic;
using System.Linq;

namespace NokitaKaze.WAVParser
{
    public static class Processing
    {

        #region Change samples rate


        public static List<short[]> ChangeSampleRate(List<short[]> wave, int sampleRate, int newSampleRate)
        {
            if (sampleRate == newSampleRate) return wave;
            List<short[]> new_sample = new List<short[]>();

            int hz1, hz2;
            {
                var nok = NOK(sampleRate, newSampleRate);

                hz1 = (int)(nok / newSampleRate);
                hz2 = (int)(nok / sampleRate);
            }

            int[] indexes;
            double[] rCoefs;
            {
                var indexesA = new List<int>(hz2);
                var rCoefsA = new List<double>(hz2);
                for (int j = 0; j < hz2; j++)
                {
                    var coef = j * 1d * hz1 / hz2;
                    var index1 = (int)Math.Floor(coef);
                    var r1 = coef - index1;

                    indexesA.Add(index1);
                    rCoefsA.Add(r1);
                }

                indexes = indexesA.ToArray();
                rCoefs = rCoefsA.ToArray();
            }

            foreach (var wav in wave)
            {
                List<short> work = new List<short>() ;
                int maxSampleCount = (int)Math.Ceiling(wav.Length * 1d * hz2 / hz1);
                
                for (int i = 0; i < wav.Length - 1; i += hz1)
                {
                    work.Add(wav[i]);
                    for (int j = 1; j < hz2; j++)
                    {
                        var index1 = indexes[j];
                        var r1 = rCoefs[j];
                        if (index1 + i + 1 >= wav.Length) break;
                        var v1 = wav[index1 + i];
                        var v2 = wav[index1 + 1 + i];
                        if (v1 == v2)
                        {
                            work.Add(v1);
                            continue;
                        }

                        // 線形補間
                        var value = (1 - r1) * v1 + r1 * v2;
                        https://qiita.com/yoya/items/f167b2598fec98679422#lanczos-lobe%E3%83%91%E3%83%A9%E3%83%A1%E3%83%BC%E3%82%BF

                        work.Add(value < short.MinValue ? short.MinValue : value > short.MaxValue ? short.MaxValue : (short)value);
                    }
                }

                while (work.Count > maxSampleCount)
                {
                    work.RemoveAt(work.Count - 1);
                }
                new_sample.Add(work.ToArray());
            }


            return new_sample;
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

            return ((long)v1 * v2 / nod);
        }

        #endregion
    }
}