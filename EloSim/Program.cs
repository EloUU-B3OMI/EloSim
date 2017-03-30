using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace EloSim
{
    public static class Tournament
    {
        /// <summary>
        /// Match delegate.
        /// </summary>
        /// <param name="round">Current round index (zero-based).</param>
        /// <param name="p1">Player 1 index (zero-based).</param>
        /// <param name="p2">Player 2 index (zero-based).</param>
        public delegate void MatchDelegate(int round, int p1, int p2);

        /// <summary>
        /// Round start delegate.
        /// </summary>
        /// <param name="round">Index of round that just started (zero-based).</param>
        public delegate void RoundDelegate(int round);

        /// <summary>
        /// Plays a round-robin tournament.
        /// </summary>
        /// <param name="numPlayers">Number of players.</param>
        /// <param name="roundDel>Round start delegate.</param>
        /// <param name="matchDel">Match delegate.</param>
        public static void RoundRobin(int numPlayers, RoundDelegate roundDel, MatchDelegate matchDel)
        {
            //http://stackoverflow.com/a/1293174/1806760
            //https://en.wikipedia.org/wiki/Round-robin_tournament#Scheduling_algorithm

            var ListTeam = new List<int>();
            for (var i = 0; i < numPlayers; i++)
                ListTeam.Add(i);

            if (numPlayers % 2 != 0)
                ListTeam.Add(-1); //Bye

            var numRounds = (numPlayers - 1);
            var halfSize = numPlayers / 2;

            var teams = new List<int>();

            teams.AddRange(ListTeam);
            teams.RemoveAt(0);

            var teamsSize = teams.Count;

            for (var round = 0; round < numRounds; round++)
            {
                if (roundDel != null)
                    roundDel(round);

                var teamIdx = round % teamsSize;
                var p1 = teams[teamIdx];
                var p2 = ListTeam[0];
                if (matchDel != null && p1 >= 0 && p2 >= 0)
                    matchDel(round, p1, p2);

                for (int idx = 1; idx < halfSize; idx++)
                {
                    int firstTeam = (round + idx) % teamsSize;
                    int secondTeam = (round + teamsSize - idx) % teamsSize;

                    p1 = teams[firstTeam];
                    p2 = teams[secondTeam];
                    if (matchDel != null && p1 >= 0 && p2 >= 0)
                        matchDel(round, p1, p2);
                }
            }
        }
    }

    /// <summary>
    /// Yahtzee client.
    /// </summary>
    public class YahtzeeClient
    {
        private NetworkStream Stream;
        private readonly Dictionary<int, ScoreCache> cache = new Dictionary<int, ScoreCache>();
        private Random rng = new Random();

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
        public YahtzeeClient(int port = 13337)
        {
            var client = new TcpClient("localhost", port);
            Stream = client.GetStream();
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
        static int N = 6;
        static int TMAX = 1000;
        static int ITER = 1;
        static double K = 14.0;
        static int[] quality = new int[N];
        static public Func<int, int, bool> game = YahtzeeWin;
        static double[,] sumelo;
        static double[,] stddevs;
        static double[] elo;
        static Random rng = new Random();
        static YahtzeeClient client;
        static int[] limitk20 = new int[] {
            95,
            68,
            28,
            21,
            67,
            100
        };
        static int[] limitk40 = new int[] {
            79,
            56,
            25,
            20,
            56,
            82
        };

        static bool AbsoluteWin(int p1, int p2)
        {
            //absolute quality (better player always wins)
            return quality[p1] > quality[p2];
        }

        static bool FlipCoinWin(int p1, int p2)
        {
            //flip a coin (50/50 the best player wins)
            return rng.NextDouble() < 0.5;
        }

        static bool ExpectedQualityWin(int p1, int p2)
        {
            //compute the expected win chance based on real quality (not Elo) and use that as a chance to win
            return (rng.NextDouble() < 1.0 / (1.0 + Math.Pow(10.0, (quality[p2] - quality[p1]) / 400.0)));
        }

        /*
        static int MapQualityFull(int player)
        {
            //maps someone's quality to [0, 100] based on the full quality range (lowest is 0, highest is 100)
            var q = (quality[player] - quality[0]) / (quality[quality.Length - 1] - quality[0]);
            return (int)(q * 100);
        }

        static int MapQualityBucket(int player)
        {
            //maps someone's quality to one of the [50, 60, 70, 80, 90, 100] buckets.
            var q = (quality[player]) / (quality[quality.Length - 1]);
            var qp = (int)(q * 100);
            return qp - qp % 10;
        }
        */

        static bool YahtzeeWin(int p1, int p2)
        {
            //play two (cached) games of yahtzee and the highest score wins
            var scoreA = client.PlayCachedGame(p1);
            var scoreB = client.PlayCachedGame(p2);
            return scoreA > scoreB;
        }

        static void roundrobin()
        {
            Tournament.RoundRobin(N, null, (round, p1, p2) =>
            {
                //https://en.wikipedia.org/wiki/Elo_rating_system#Mathematical_details

                //compute expected scores (winning chances)
                var Ra = elo[p1];
                var Rb = elo[p2];
                var Ea = 1.0 / (1.0 + Math.Pow(10.0, (Rb - Ra) / 400.0));
                var Eb = 1.0 / (1.0 + Math.Pow(10.0, (Ra - Rb) / 400.0));

                //compute outcomes for A and B (lose: 0.0, draw: 0.5, win: 1.0)
                double Sa, Sb;
                if (game(quality[p1], quality[p2])) //we don't consider draws because they have no influence on the bigger picture
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
                elo[p1] = Rna;
                elo[p2] = Rnb;
            });
        }

        static void output(string filename)
        {
            using (var fileStream = new FileStream(filename, FileMode.Create))
            {
                using (var outputStream = new StreamWriter(fileStream))
                {
                    //print sumelo after every time step
                    for (var t = 0; t < TMAX; t++)
                    {
                        var tavg = 0.0;
                        for (var i = 0; i < N; i++)
                            tavg += sumelo[i, t];
                        tavg /= N;
                        outputStream.Write($"{t}");
                        for (var i = 0; i < N; i++)
                            outputStream.Write($"\t{(sumelo[i, t] / ITER)}");
                        outputStream.WriteLine();
                    }

                    outputStream.WriteLine();

                    //print actual quality for reference
                    for (var i = 0; i < N; i++)
                        outputStream.Write($"\t{(int)quality[i]}");
                }
            }
        }

        static void outputstddev(string filename)
        {
            using (var fileStream = new FileStream(filename, FileMode.Create))
            {
                using (var outputStream = new StreamWriter(fileStream))
                {
                    //print sumelo after every time step
                    for (var t = 0; t < TMAX; t++)
                    {
                        var tavg = 0.0;
                        for (var i = 0; i < N; i++)
                            tavg += sumelo[i, t];
                        tavg /= N;
                        outputStream.Write($"{t}");
                        for (var i = 0; i < N; i++)
                            outputStream.Write($"\t{(stddevs[i, t])}");
                        outputStream.WriteLine();
                    }
                }
            }
        }

        public struct ExperimentSettings
        {
            public enum ExperimentType
            {
                Mean,
                StandardDeviation
            }

            public ExperimentType Type { get; set; }
            public int NumIterations { get; set; }
            public int NumRounds { get; set; }
            public double KVal { get; set; }
            public List<int> PlayerSkills { get; set; }
            public Func<int, int, bool> Game { get; set; }
            public bool ProduceOutput { get; set; }
        }

        public static void Experiment(ExperimentSettings es)
        {
            // Setup
            ITER = es.NumIterations;
            TMAX = es.NumRounds;
            N = es.PlayerSkills.Count();
            K = es.KVal;
            quality = es.PlayerSkills.ToArray();
            game = es.Game;

            sumelo = new double[N, TMAX];
            elo = new double[N];

            //Run the yahtzee-master server
            client = new YahtzeeClient();

            switch (es.Type)
            {
                case ExperimentSettings.ExperimentType.Mean:
                    {
                        var q_av = 0.0;
                        for (var i = 0; i < N; i++) //average quality
                            q_av += quality[i];
                        q_av /= N;
                        Console.Error.WriteLine($"q_av: {q_av}");

                        for (var i = 0; i < N; i++)
                            for (var t = 0; t < TMAX; t++)
                                sumelo[i, t] = 0;

                        for (var it = 0; it < ITER; it++)
                        {
                            for (var i = 0; i < N; i++)
                                elo[i] = (int)q_av;
                            for (var t = 0; t < TMAX; t++)
                            {
                                roundrobin();
                                for (var i = 0; i < N; i++)
                                    sumelo[i, t] += elo[i];
                            }
                        }

                        if (es.ProduceOutput)
                        {
                            string outputFilename =
                                string.Format("output-i={0}-t={1}-k={2}-skills={3}.txt",
                                              es.NumIterations,
                                              es.NumRounds,
                                              es.KVal,
                                              string.Join("+", es.PlayerSkills.Select(s => s.ToString())));

                            output(outputFilename);
                        }
                    }
                    break;

                case ExperimentSettings.ExperimentType.StandardDeviation:
                    {
                        stddevs = new double[N, es.NumRounds];
                        for (int i = 0; i < es.NumIterations; i++)
                        {
                            for (int p = 0; p < N; p++)
                            {
                                Experiment(new ExperimentSettings
                                {
                                    Type = ExperimentSettings.ExperimentType.Mean,
                                    Game = es.Game,
                                    NumIterations = 1,
                                    KVal = es.KVal,
                                    NumRounds = es.NumRounds,
                                    PlayerSkills = es.PlayerSkills,
                                    ProduceOutput = false
                                });


                                // HAX HAX HAX \/
                                int offset = (K < 25) ? limitk20[p] : limitk40[p];
                                int sz = es.NumRounds - offset;

                                double mean = 0;
                                for (int j = 0; j < sz; j++)
                                    mean += sumelo[p, j + offset];
                                mean /= sz;

                                var deviances = new double[sz];
                                // HAX HAX HAX \/
                                for (int j = 0; j < sz; j++)
                                    deviances[j] = Math.Pow((sumelo[p, j + offset] - mean), 2);

                                double avg = deviances.Average();
                                double stddev = Math.Sqrt(avg);
                                stddevs[p, i] = stddev;
                            }
                        }

                        if (es.ProduceOutput)
                        {
                            string outputFilename =
                                string.Format("stddevs-i={0}-t={1}-k={2}-skills={3}.txt",
                                              es.NumIterations,
                                              es.NumRounds,
                                              es.KVal,
                                              string.Join("+", es.PlayerSkills.Select(s => s.ToString())));

                            outputstddev(outputFilename);
                        }
                    }
                    break;
            }
        }

        public static void Main(string[] args)
        {
            //BuildCache();
            MainCalculateChances();
            //MainEloDevelopment();
        }

        public static void BuildCache()
        {
            client = new YahtzeeClient();
            var players = new List<int> { 10, 20, 30, 40 };
            int nsamples = 10 * 1000;

            for (int i = 0; i < players.Count(); i++)
            {
                var filename = $"samples-d{players[i]}.dat";
                var filestream = new FileStream(filename, FileMode.CreateNew);
                Console.WriteLine($"i={i}");
                using (var writer = new StreamWriter(filestream))
                {
                    for (int j = 0; j < nsamples; j++)
                    {
                        if (j % 100 == 0)
                            Console.WriteLine(j / 100);

                        var score = client.PlaySingleGame(players[i]);
                        writer.WriteLine(score);
                    }
                }
            }
        }

        public static void MainCalculateChances()
        {
            const bool onlyelocalc = true;
            client = new YahtzeeClient();
            var players = new List<int> { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
            var results = new List<double>();
            const int rounds = 10000;
            for (int i = 0; i < (onlyelocalc ? players.Count() - 1 : players.Count()); i++)
            {
                int start = i + 1;
                int until = players.Count;

                if (onlyelocalc)
                {
                    start = i + 1;
                    until = i + 1 + 1;
                }

                for (int j = start; j < until; j++)
                {
                    //if (i == i2 - 1)
                    //	continue;

                    int pq1 = players[i] * 20; // hax
                    int pq2 = players[j] * 20; // hax
                    double elo1 = 1000;
                    double elo2 = 1000;
                    int p1wins = 0;

                    var roundresults = new double[rounds];

                    using (var writer = new StreamWriter(new FileStream($"elos-p{i + 1}-vs-p{j + 1}.dat", FileMode.Create)))
                    {
                        for (int k = 0; k < rounds; k++)
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

                        // STANDARD ERROR CALCULATION

                        double mean = 0;
                        for (int k = 0; k < rounds; k++)
                            mean += roundresults[k];
                        mean /= rounds;

                        var deviances = new double[rounds];
                        for (int k = 0; k < rounds; k++)
                            deviances[k] = Math.Pow((roundresults[k] - mean), 2);

                        double avg = deviances.Average();
                        double stddev = Math.Sqrt(avg);

                        double chance = ((double)p1wins / (double)rounds);
                        results.Add(chance);
                        Console.WriteLine($"{i}-{j}\t{chance}\t{elo1}\t{elo2}\t{stddev}");
                    }
                }
            }
        }

        public static void MainEloDevelopment()
        {
            const int n = 1000;
            const int iter = 1000;

            var players = new List<int> {
                1000, 1200, 1400, 1600, 1800, 2000
            };

            var ks = new List<double> { 20.0, 40.0 };

            foreach (var k in ks)
            {
                Experiment(new ExperimentSettings
                {
                    Type = ExperimentSettings.ExperimentType.Mean,
                    Game = YahtzeeWin,
                    NumIterations = iter,
                    NumRounds = n,
                    KVal = k,
                    PlayerSkills = players,
                    ProduceOutput = true
                });

                Experiment(new ExperimentSettings
                {
                    Type = ExperimentSettings.ExperimentType.StandardDeviation,
                    Game = YahtzeeWin,
                    NumIterations = iter,
                    NumRounds = n,
                    KVal = k,
                    PlayerSkills = players,
                    ProduceOutput = true
                });
            }
        }
    }
}
