using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;

namespace EloSim
{
    /// <summary>
    /// Yahtzee client.
    /// </summary>
    public class YahtzeeClient
    {
        private NetworkStream Stream;
        private readonly Dictionary<int, ScoreCache> cache = new Dictionary<int, ScoreCache>();
        private readonly Random rng = new Random();

        private class ScoreCache
        {
            public int[] Data;
            public int Index;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:EloSim.YahtzeeClient"/> class.
        /// Returns after the connection is established.
        /// </summary>
        /// <param name="port">Port to connect over.</param>
        public YahtzeeClient(bool useServer = false, int port = 13337)
        {
			if (useServer)
			{
				var client = new TcpClient("localhost", port);
				Stream = client.GetStream();
			}
        }

        /// <summary>
        /// Plaies the single game.
        /// Protocol: Send quality (1 byte), receive score (2 bytes, big endian).
        /// </summary>
        /// <returns>The score of the game</returns>
        /// <param name="q">Quality.</param>
        public int PlaySingleGame(int q)
        {
            //Console.Write("PlaySingleGame({0}) -> ", q);
            Stream.WriteByte((byte)q);
            var bytes = new byte[2];
            for (var i = 0; i < bytes.Length; i++)
            {
                var x = Stream.ReadByte();
                if (x == -1)
                    throw new InvalidOperationException();
                bytes[i] = (byte)x;
            }
            var score = bytes[0] << 8 | bytes[1];
            //.WriteLine(score);
            return score;
        }

        public int MapQuality(int q)
        {
            return q / 20;
        }

        public int PlayCachedGame(int q)
        {
            int realQ = MapQuality(q);

            if (!cache.ContainsKey(q))
            {
                //Copy the samples to the bin/Debug directory
                cache[q] = new ScoreCache
                {
                    Data = File.ReadAllLines($"samples-d{realQ}.dat").Select(int.Parse).ToArray(),
                    Index = 0
                };
            }
            var data = cache[q];
            return data.Data[rng.Next(data.Data.Length)];
        }
    }

    class MainClass
    {
		private enum Mode
		{
			CalculateElosForSubsequentMatches,
			EmulateAllPossibleMatches
		}

        static readonly double K = 14.0;
		static readonly int N = 10000;
		static readonly List<double> Players = new List<double> { 0.10, 0.20, 0.30, 0.40, 0.50, 0.60, 0.70, 0.80, 0.90, 1.00 };
		static readonly Mode Modus = Mode.CalculateElosForSubsequentMatches;
		static readonly bool NeedServer = false;

        static Random rng = new Random();
        static YahtzeeClient client;

        static bool FlipCoinWin(int p1, int p2)
        {
            //flip a coin (50/50 the best player wins)
            return rng.NextDouble() < 0.5;
        }

        static bool YahtzeeWin(int p1, int p2)
        {
            //play two (cached) games of yahtzee and the highest score wins
            var scoreA = client.PlayCachedGame(p1);
            var scoreB = client.PlayCachedGame(p2);
            return scoreA > scoreB;
        }

        public static void Main()
        {
			client = new YahtzeeClient(NeedServer);
            var results = new List<double>();

			for (int i = 0; i < (Modus == Mode.CalculateElosForSubsequentMatches ? Players.Count() - 1 : Players.Count()); i++)
            {
                int start = i + 1;
                int until = Players.Count;

                if (Modus == Mode.CalculateElosForSubsequentMatches)
                {
                    start = i + 1;
                    until = i + 1 + 1;
                }

                for (int j = start; j < until; j++)
                {
                    //if (i == i2 - 1)
                    //	continue;

					int pq1 = (int) (Players[i] * 2000.0); // hax
					int pq2 = (int) (Players[j] * 2000.0); // hax
                    double elo1 = 1000;
                    double elo2 = 1000;
                    int p1wins = 0;

                    var roundresults = new double[N];

                    using (var writer = new StreamWriter(new FileStream($"elos-p{i + 1}-vs-p{j + 1}.dat", FileMode.Create)))
                    {
                        for (int k = 0; k < N; k++)
                        {
                            bool win = YahtzeeWin(pq1, pq2);

                            if (win)
                            {
                                p1wins++;
                            }

                            //compute expected scores (winning chances)
                            var Ra = elo1;
                            var Rb = elo2;
                            var Ea = 1.0 / (1.0 + Math.Pow(10.0, (Rb - Ra) / 400.0));
                            var Eb = 1.0 / (1.0 + Math.Pow(10.0, (Ra - Rb) / 400.0));

                            //compute outcomes for A and B (lose: 0.0, draw: 0.5, win: 1.0)
                            double Sa, Sb;
                            if (win) //we don't consider draws because they have no influence on the bigger picture
                            {
                                Sa = 1.0;
                                Sb = 0.0;
                            }
                            else
                            {
                                Sa = 0.0;
                                Sb = 1.0;
                            }

                            //compute new ratings
                            var Rna = (Ra + K * (Sa - Ea));
                            var Rnb = (Rb + K * (Sb - Eb));
                            //Console.WriteLine($"{p1} ({Ra} -> {Rna}) vs {p2} ({Rb} -> {Rnb})");

                            //update ratings
                            elo1 = Rna;
                            elo2 = Rnb;

                            roundresults[k] = Sa;

                            if (k > 9000)
                                writer.WriteLine($"{elo1}\t{elo2}");
                        }

                        // calculate standard error
                        double mean = 0;
                        for (int k = 0; k < N; k++)
                            mean += roundresults[k];
                        mean /= N;

                        var deviances = new double[N];
                        for (int k = 0; k < N; k++)
                            deviances[k] = Math.Pow((roundresults[k] - mean), 2);

                        double avg = deviances.Average();
                        double stddev = Math.Sqrt(avg);

                        double chance = ((double)p1wins / (double)N);
                        results.Add(chance);
                        Console.WriteLine($"{i}-{j}\t{chance}\t{elo1}\t{elo2}\t{stddev}");
                    }
                }
            }
        }
    }
}
