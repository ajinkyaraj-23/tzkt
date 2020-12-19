﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Tzkt.Data.Models;

namespace Tzkt.Sync.Protocols.Proto1
{
    partial class ProtoActivator : ProtocolCommit
    {
        public void BootstrapVoting(Protocol protocol, List<Account> accounts)
        {
            var snapshots = accounts
                .Where(x => x.Type == AccountType.Delegate)
                .Select(x => x as Delegate)
                .Select(x => new VotingSnapshot
                {
                    Level = 1,
                    Period = 0,
                    BakerId = x.Id,
                    Rolls = (int)(x.StakingBalance / protocol.TokensPerRoll),
                    Status = VoterStatus.None
                });

            var period = new VotingPeriod
            {
                Index = 0,
                Epoch = 0,
                FirstLevel = 1,
                LastLevel = protocol.BlocksPerVoting,
                Kind = PeriodKind.Proposal,
                Status = PeriodStatus.Active,
                TotalBakers = snapshots.Count(),
                TotalRolls = snapshots.Sum(x => x.Rolls),
                UpvotesQuorum = protocol.ProposalQuorum,
                ProposalsCount = 0,
                TopUpvotes = 0,
                TopRolls = 0
            };

            Db.VotingSnapshots.AddRange(snapshots);
            Db.VotingPeriods.Add(period);
            Cache.Periods.Add(period);
        }

        public async Task ClearVoting()
        {
            await Db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""VotingPeriods"";
                DELETE FROM ""VotingSnapshots"";");
            Cache.Periods.Reset();
        }
    }
}
