// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using McMaster.Extensions.CommandLineUtils;
using osu.Server.QueueProcessor;

namespace osu.ElasticIndexer.Commands.Schema
{
    [Command("set", Description = "Sets the current index schema version to use.")]
    public class SchemaVersionSet
    {
        [Argument(0, "schema", "The schema version to set as current.")]
        [Required]
        public string Schema { get; set; } = string.Empty;

        public int OnExecute(CancellationToken token)
        {
            RedisAccess.GetConnection().SetCurrentSchema(Schema);
            Console.WriteLine(ConsoleColor.Yellow, $"Schema version set to {Schema}");
            return 0;
        }
    }
}
