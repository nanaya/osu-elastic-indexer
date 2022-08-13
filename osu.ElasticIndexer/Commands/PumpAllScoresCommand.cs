// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using McMaster.Extensions.CommandLineUtils;

namespace osu.ElasticIndexer.Commands
{
    [Command("all", Description = "Pumps scores through the queue for processing")]
    public class PumpAllScoresCommand : ProcessorCommandBase
    {
        [Option("--delay", Description = "Delay in milliseconds between reading chunks")]
        public int Delay { get; set; }

        [Option("--from", Description = "Score id to resume from")]
        public long? From { get; set; }

        [Option("--switch", Description = "Update the configured schema in redis after completing")]
        public bool Switch { get; set; }

        private CancellationToken cancellationToken;

        public int OnExecute(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(AppSettings.Schema))
                throw new MissingSchemaException();

            this.cancellationToken = cancellationToken;

            var redis = new Redis();
            var currentSchema = redis.GetSchemaVersion();
            Console.WriteLine(ConsoleColor.Green, $"Current schema version is: {currentSchema}");
            Console.WriteLine(ConsoleColor.Green, $"Pushing to queue with schema: {AppSettings.Schema}");

            if (Switch && currentSchema == AppSettings.Schema)
                Console.WriteLine(ConsoleColor.Yellow, "Queue watchers will not update the alias if schema does not change!");

            var startTime = DateTimeOffset.Now;
            Console.WriteLine(ConsoleColor.Cyan, $"Start read: {startTime}");

            var lastId = queueScores(From);

            var endTime = DateTimeOffset.Now;
            Console.WriteLine(ConsoleColor.Cyan, $"End read: {endTime}, time taken: {endTime - startTime}");

            if (Switch)
            {
                redis.SetSchemaVersion(AppSettings.Schema);
                Console.WriteLine(ConsoleColor.Yellow, $"Schema version set to {AppSettings.Schema}, queueing scores > {lastId}");
                queueScores(lastId);

                var switchEndTime = DateTimeOffset.Now;
                Console.WriteLine(ConsoleColor.Cyan, $"End read after switch: {switchEndTime}, time taken: {switchEndTime - startTime}");
            }

            return 0;
        }

        private long? queueScores(long? from)
        {
            var chunks = ElasticModel.Chunk<SoloScore>(AppSettings.BatchSize, from);
            SoloScore? last = null;

            foreach (var scores in chunks)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                List<ScoreItem> scoreItems = new List<ScoreItem>();

                foreach (var score in scores)
                {
                    score.country_code ??= "XX";
                    scoreItems.Add(new ScoreItem { Score = score });
                }

                Console.WriteLine($"Pushing {scoreItems.Count} scores");

                Processor.PushToQueue(scoreItems);

                Console.WriteLine($"Pushed {scores.LastOrDefault()}");
                last = scores.LastOrDefault();

                if (Delay > 0)
                    Thread.Sleep(Delay);
            }

            return last?.id;
        }
    }
}
