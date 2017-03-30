using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Linq;
using System.Text;

namespace CacheGenerator
{
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
        public static void Main(string[] args)
        {
            var client = new YahtzeeClient();
            var i = int.Parse(args[0]);
            using (var s = new StreamWriter($"samples-d{i}.dat", true))
                for (var j = 0; j < 10000; j++)
                    s.WriteLine(client.PlaySingleGame(i));
        }
    }
}
