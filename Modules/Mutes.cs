﻿using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MicrosoftBot.Modules
{

    public static class Mutes
    {

        public static TimeSpan RoundToNearest(this TimeSpan a, TimeSpan roundTo)
        {
            long ticks = (long)(Math.Round(a.Ticks / (double)roundTo.Ticks) * roundTo.Ticks);
            return new TimeSpan(ticks);
        }

        // Only to be used on naughty users.
        public static async System.Threading.Tasks.Task<bool> MuteUserAsync(DiscordMember naughtyMember, TimeSpan muteDuration, string reason, ulong moderatorId, DiscordGuild guild)
        {
            DiscordChannel logChannel = await Program.discord.GetChannelAsync(Program.cfgjson.LogChannel);
            DiscordRole mutedRole = guild.GetRole(Program.cfgjson.MutedRole);
            DateTime expireTime = DateTime.Now + muteDuration;
            MemberMute newMute = new MemberMute()
            {
                MemberId = naughtyMember.Id,
                ExpireTime = expireTime,
                ModId = moderatorId,
                ServerId = guild.Id
            };

            await Program.db.HashSetAsync("mutes", naughtyMember.Id, JsonConvert.SerializeObject(newMute));

            try {
                await naughtyMember.GrantRoleAsync(mutedRole);
            } catch
            {
                return false;
            }

            await logChannel.SendMessageAsync($"{Program.cfgjson.Emoji.Muted} Successfully muted {naughtyMember.Mention} until `{expireTime}` (In roughly {muteDuration.TotalHours} hours)");
            return true;
        }

        public static async System.Threading.Tasks.Task<bool> CheckMutesAsync()
        {
            DiscordChannel logChannel = await Program.discord.GetChannelAsync(Program.cfgjson.LogChannel);
            Dictionary<string, MemberMute> muteList = Program.db.HashGetAll("mutes").ToDictionary(
                x => x.Name.ToString(),
                x => JsonConvert.DeserializeObject<MemberMute>(x.Value)
            );
            if (muteList == null | muteList.Keys.Count == 0)
                return false;
            else
            {
                // The success value will be changed later if any of the unmutes are successful.
                bool success = false;
                foreach (KeyValuePair<string, MemberMute> entry in muteList)
                {
                    MemberMute mute = entry.Value;
                    if (DateTime.Now > mute.ExpireTime)
                    {
                        DiscordGuild guild = await Program.discord.GetGuildAsync(mute.ServerId);

                        // todo: store per-guild
                        DiscordRole mutedRole = guild.GetRole(Program.cfgjson.MutedRole);
                        DiscordMember member = await guild.GetMemberAsync(mute.MemberId);
                        if (member == null)
                        {
                            await logChannel.SendMessageAsync($"{Program.cfgjson.Emoji.Error} Attempt to unmute <@{mute.MemberId}> failed!" +
                                $"Is the user in the server?");
                        }
                        else
                        {
                            // Perhaps we could be catching something specific, but this should do for now.
                            try
                            {
                                await member.RevokeRoleAsync(mutedRole);
                                await logChannel.SendMessageAsync($"{Program.cfgjson.Emoji.Information} Automatically unmuted <@{mute.MemberId}>!");
                            }
                            catch
                            {
                                await logChannel.SendMessageAsync($"{Program.cfgjson.Emoji.Error} Attempt to removed Muted role from <@{mute.MemberId}> failed!" +
                                $"\nIf the role was removed manually, this error can be disregarded safely.");
                            }
                        }
                        // Even if the bot failed to remove the role, it reported that failure to a log channel and thus the mute
                        //  can be safely removed internally.
                        await Program.db.HashDeleteAsync("mutes", entry.Key);

                        success = true;
                    }
                }
#if DEBUG
                Console.WriteLine($"Checked mutes at {DateTime.Now} with result: {success}");
#endif
                return success;
            }
        }
    }
}
