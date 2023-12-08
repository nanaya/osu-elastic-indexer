// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Elasticsearch.Net;
using Elasticsearch.Net.Specification.IndicesApi;
using Nest;

namespace osu.ElasticIndexer
{
    public class OsuElasticClient : ElasticClient
    {
        public readonly string AliasName = $"{AppSettings.Prefix}scores";

        public OsuElasticClient(bool throwsExceptions = true)
            : base(new ConnectionSettings(new Uri(AppSettings.ElasticsearchHost))
                   .EnableApiVersioningHeader()
                   .ThrowExceptions(throwsExceptions))
        {
        }

        /// <summary>
        /// Attempts to find the matching index or creates a new one.
        /// </summary>
        /// <param name="name">name of the index alias.</param>
        /// <returns>Name of index found or created and any existing alias.</returns>
        public IndexMetadata FindOrCreateIndex(string name)
        {
            Console.WriteLine();

            var indices = GetIndicesForVersion(name, AppSettings.Schema);

            // 3 cases are handled:
            if (indices.Count > 0)
            {
                // 1. Index was already aliased; likely resuming from a completed job.
                var (indexName, indexState) = indices.FirstOrDefault(entry => entry.Value.Aliases.ContainsKey(name));

                if (indexName != null)
                {
                    Console.WriteLine(ConsoleColor.Cyan, $"Using aliased `{indexName}`.");

                    return new IndexMetadata(indexName, indexState);
                }

                // 2. Index has not been aliased and has tracking information;
                // likely resuming from an incomplete job or waiting to switch over.
                // TODO: throw if there's more than one? or take lastest one.
                (indexName, indexState) = indices.First();
                Console.WriteLine(ConsoleColor.Cyan, $"Using non-aliased `{indexName}`.");

                return new IndexMetadata(indexName, indexState);
            }

            // 3. no existing index
            return createIndex(name);
        }

        public IReadOnlyDictionary<IndexName, IndexState> GetIndex(string name)
        {
            return Indices.Get(name).Indices;
        }

        public IReadOnlyDictionary<IndexName, IndexState> GetIndices(string name, ExpandWildcards expandWildCards = ExpandWildcards.Open)
        {
            return Indices.Get($"{name}_*", descriptor => descriptor.ExpandWildcards(expandWildCards)).Indices;
        }

        public List<KeyValuePair<IndexName, IndexState>> GetIndicesForVersion(string name, string schema)
        {
            return GetIndices(name)
                   .Where(entry => (string?)entry.Value.Mappings.Meta?["schema"] == schema)
                   .ToList();
        }

        public void UpdateAlias(string alias, string index, bool close = true)
        {
            // TODO: updating alias should mark the index as ready since it's switching over.
            Console.WriteLine(ConsoleColor.Yellow, $"Updating `{alias}` alias to `{index}`...");

            var aliasDescriptor = new BulkAliasDescriptor();
            var oldIndices = this.GetIndicesPointingToAlias(alias);

            foreach (var oldIndex in oldIndices)
                aliasDescriptor.Remove(d => d.Alias(alias).Index(oldIndex));

            aliasDescriptor.Add(d => d.Alias(alias).Index(index));
            Indices.BulkAlias(aliasDescriptor);

            // cleanup
            if (!close) return;

            foreach (var toClose in oldIndices.Where(x => x != index))
            {
                Console.WriteLine(ConsoleColor.Yellow, $"Closing {toClose}");
                Indices.Close(toClose);
            }
        }

        private IndexMetadata createIndex(string name)
        {
            var suffix = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            var index = $"{name}_{suffix}";

            Console.WriteLine(ConsoleColor.Cyan, $"Creating `{index}` for `{name}`.");

            var json = File.ReadAllText(Path.GetFullPath("schemas/scores.json"));
            LowLevel.Indices.Create<DynamicResponse>(
                index,
                json,
                new CreateIndexRequestParameters { WaitForActiveShards = "all" }
            );
            var metadata = new IndexMetadata(index, AppSettings.Schema);

            metadata.Save(this);

            return metadata;
        }
    }
}
