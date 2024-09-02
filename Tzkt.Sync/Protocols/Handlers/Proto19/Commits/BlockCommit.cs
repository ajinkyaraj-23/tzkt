﻿using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tzkt.Data.Models;

namespace Tzkt.Sync.Protocols.Proto19
{
    class BlockCommit : Proto18.BlockCommit
    {
        public BlockCommit(ProtocolHandler protocol) : base(protocol) { }

        protected override (long, long, long, long, long, long, long, long) ParseRewards(Data.Models.Delegate proposer, Data.Models.Delegate producer, List<JsonElement> balanceUpdates)
        {
            var rewardDelegated = 0L;
            var rewardStakedOwn = 0L;
            var rewardStakedEdge = 0L;
            var rewardStakedShared = 0L;
            var bonusDelegated = 0L;
            var bonusStakedOwn = 0L;
            var bonusStakedEdge = 0L;
            var bonusStakedShared = 0L;

            for (int i = 0; i < balanceUpdates.Count; i++)
            {
                var update = balanceUpdates[i];
                if (update.RequiredString("kind") == "minted" && update.RequiredString("category") == "baking rewards")
                {
                    if (i == balanceUpdates.Count - 1)
                        throw new Exception("Unexpected baking rewards balance updates behavior");

                    var change = -update.RequiredInt64("change");

                    var nextUpdate = balanceUpdates[i + 1];
                    if (nextUpdate.RequiredInt64("change") != change)
                        throw new Exception("Unexpected baking rewards balance updates behavior");

                    if (nextUpdate.RequiredString("kind") == "freezer" && nextUpdate.RequiredString("category") == "deposits")
                    {
                        var staker = nextUpdate.Required("staker");
                        if (staker.TryGetProperty("baker_own_stake", out var p) && p.GetString() == proposer.Address)
                        {
                            rewardStakedOwn += change;
                        }
                        else if (staker.TryGetProperty("baker_edge", out p) && p.GetString() == proposer.Address)
                        {
                            rewardStakedEdge += change;
                        }
                        else if (staker.TryGetProperty("delegate", out p) && p.GetString() == proposer.Address)
                        {
                            rewardStakedShared += change;
                        }
                        else
                        {
                            throw new Exception("Unexpected baking rewards balance updates behavior");
                        }
                    }
                    else if (nextUpdate.RequiredString("kind") == "contract" && nextUpdate.RequiredString("contract") == proposer.Address)
                    {
                        rewardDelegated += change;
                    }
                    else
                    {
                        throw new Exception("Unexpected baking rewards balance updates behavior");
                    }
                }
                else if (update.RequiredString("kind") == "minted" && update.RequiredString("category") == "baking bonuses")
                {
                    if (i == balanceUpdates.Count - 1)
                        throw new Exception("Unexpected baking bonuses balance updates behavior");

                    var change = -update.RequiredInt64("change");

                    var nextUpdate = balanceUpdates[i + 1];
                    if (nextUpdate.RequiredInt64("change") != change)
                        throw new Exception("Unexpected baking bonuses balance updates behavior");

                    if (nextUpdate.RequiredString("kind") == "freezer" && nextUpdate.RequiredString("category") == "deposits")
                    {
                        var staker = nextUpdate.Required("staker");
                        if (staker.TryGetProperty("baker_own_stake", out var p) && p.GetString() == producer.Address)
                        {
                            bonusStakedOwn += change;
                        }
                        else if (staker.TryGetProperty("baker_edge", out p) && p.GetString() == producer.Address)
                        {
                            bonusStakedEdge += change;
                        }
                        else if (staker.TryGetProperty("delegate", out p) && p.GetString() == producer.Address)
                        {
                            bonusStakedShared += change;
                        }
                        else
                        {
                            throw new Exception("Unexpected baking bonuses balance updates behavior");
                        }
                    }
                    else if (nextUpdate.RequiredString("kind") == "contract" && nextUpdate.RequiredString("contract") == producer.Address)
                    {
                        bonusDelegated += change;
                    }
                    else
                    {
                        throw new Exception("Unexpected baking bonuses balance updates behavior");
                    }
                }
            }

            return (rewardDelegated, rewardStakedOwn, rewardStakedEdge, rewardStakedShared, bonusDelegated, bonusStakedOwn, bonusStakedEdge, bonusStakedShared);
        }

        public virtual async Task UpdateDalCommitmentStatus(JsonElement rawBlock)
        {
            var level  = rawBlock.Required("header").RequiredInt32("level");
            var dalAttStatusList = Cache.DalAttestations.GetCached(level);
            if (dalAttStatusList.Count == 0) return;
            var dalStatusEntries = dalAttStatusList.Where(das => das.Attested ).GroupBy(das =>
                new { publishCommitmentId = das.DalCommitmentStatusId})
            .Select(group => new
            {
                CommitmentStatusId = group.Key.publishCommitmentId,
                ShardsCount = group.Sum(das => das.ShardsCount)
            }).ToList();
            // These DalAttestations were done for DAL commitments published at level exactly DalAttestationLag before current level.
            var statusEntries =
                await Cache.DalCommitmentStatus.GetOrDefaultAsync(level - Block.Protocol.DalAttestationLag);
            var shardsThreshold = Math.Round((Block.Protocol.DalAttestationThreshold / 100.0f) *
                                                                                       (Block.Protocol.DalShardsPerSlot), MidpointRounding.AwayFromZero);
            foreach (var item in dalStatusEntries)
            {
                var statusEntry = statusEntries.Find(d => d.Id == item.CommitmentStatusId);
                statusEntry.ShardsAttested = item.ShardsCount;
                statusEntry.Attested = (item.ShardsCount >= shardsThreshold);
            }
            
            Db.DalCommitmentStatus.UpdateRange(statusEntries);
        }

        public virtual async Task RevertDalCommitmentStatusUpdate(Block block)
        {
            // Revert the status updates done to DalCommitmentStatus table for rows which were updated due to attestations
            var commitmentStatus =
                await Cache.DalCommitmentStatus.GetOrDefaultAsync(block.Level - block.Protocol.DalAttestationLag);
            if (commitmentStatus != null)
            {
                commitmentStatus.ForEach(commit =>
                {
                    commit.ShardsAttested = 0;
                    commit.Attested = false;
                });
                Db.DalCommitmentStatus.UpdateRange(commitmentStatus);
                await Db.SaveChangesAsync();
            }
        } 
    }
}