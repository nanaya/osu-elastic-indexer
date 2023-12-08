// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using osu.Server.QueueProcessor;
using StackExchange.Redis;

namespace osu.ElasticIndexer
{
    public class SoloScoreIndexer
    {
        private CancellationTokenSource? cts;
        private IndexMetadata? metadata;
        private string? previousSchema;

        private readonly OsuElasticClient elasticClient = new OsuElasticClient();
        private readonly ConnectionMultiplexer redis = RedisAccess.GetConnection();

        public SoloScoreIndexer()
        {
            if (string.IsNullOrEmpty(AppSettings.Schema))
                throw new MissingSchemaException();
        }

        public void Run(CancellationToken token)
        {
            using (cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                metadata = elasticClient.FindOrCreateIndex(elasticClient.AliasName);

                checkSchema();

                redis.AddActiveSchema(metadata.Name);

                if (string.IsNullOrEmpty(redis.GetCurrentSchema()))
                    redis.SetCurrentSchema(metadata.Name);

                using (new Timer(_ => checkSchema(), null, TimeSpan.Zero, TimeSpan.FromSeconds(5)))
                    new IndexQueueProcessor(metadata.Name, elasticClient, Stop).Run(cts.Token);
            }
        }

        public void Stop()
        {
            if (cts == null || cts.IsCancellationRequested)
                return;

            cts.Cancel();
        }

        private void checkSchema()
        {
            try
            {
                string schema = redis.GetCurrentSchema();

                previousSchema ??= schema;

                if (previousSchema == schema)
                    return;

                // schema has changed to the current one
                if (previousSchema != schema && schema == AppSettings.Schema)
                {
                    Console.WriteLine(ConsoleColor.Yellow, $"Schema switched to current: {schema}");
                    previousSchema = schema;
                    elasticClient.UpdateAlias(elasticClient.AliasName, metadata!.Name);
                    return;
                }

                Console.WriteLine(ConsoleColor.Yellow, $"Previous schema {previousSchema}, got {schema}, need {AppSettings.Schema}, exiting...");
                redis.RemoveActiveSchema(AppSettings.Schema);
                Stop();
            }
            catch (Exception e)
            {
                System.Console.WriteLine($"Schema check failed ({e})");
            }
        }
    }
}
