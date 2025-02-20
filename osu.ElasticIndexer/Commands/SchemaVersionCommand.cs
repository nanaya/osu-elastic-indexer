// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading;
using McMaster.Extensions.CommandLineUtils;

namespace osu.ElasticIndexer.Commands
{
    [Command("schema", Description = "Gets the current index schema version to use")]
    [Subcommand(typeof(SchemaVersionClear))]
    [Subcommand(typeof(SchemaVersionSet))]
    public class SchemaVersionCommand
    {
        public int OnExecute(CancellationToken token)
        {
            var value = new Redis().GetSchemaVersion();
            Console.WriteLine($"Current schema version is {value}");
            return 0;
        }
    }
}
