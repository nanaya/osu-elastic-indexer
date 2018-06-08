// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-elastic-indexer/master/LICENCE

using Dapper.Contrib.Extensions;

namespace ElasticIndex
{
    [Table("osu_scores_taiko_high")]
    public class HighScoreTaiko : HighScore
    {
    }
}
