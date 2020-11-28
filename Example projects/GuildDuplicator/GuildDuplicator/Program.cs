﻿using Discord;
using Discord.Gateway;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace GuildDuplicator
{
    class Program
    {
        static void Main()
        {
            DiscordSocketClient client = new DiscordSocketClient();
            client.OnLoggedIn += Client_OnLoggedIn;

            Console.Write("Token: ");
            client.Login(Console.ReadLine());

            Thread.Sleep(-1);
        }

        private static void Client_OnLoggedIn(DiscordSocketClient client, LoginEventArgs args)
        {
            Console.Write("Server (ID) to copy from: ");

            SocketGuild guild = client.GetCachedGuild(ulong.Parse(Console.ReadLine())); // u could also just grab from args.Guilds, but i prefer this method bcuz we can be sure that the info is up to date

            Console.WriteLine("Duplicating guild...");
            DiscordGuild ourGuild = client.CreateGuild(guild.Name, guild.Icon == null ? null : guild.Icon.Download(), guild.Region);
            ourGuild.Modify(new GuildProperties() { DefaultNotifications = guild.DefaultNotifications, VerificationLevel = guild.VerificationLevel });

            Console.WriteLine("Duplicating roles...");
            Dictionary<ulong, ulong> dupedRoles = new Dictionary<ulong, ulong>();
            foreach (var role in guild.Roles.OrderBy(r => r.Position).Reverse())
                dupedRoles.Add(role.Id, ourGuild.CreateRole(new RoleProperties() { Name = role.Name, Color = role.Color, Mentionable = role.Mentionable, Permissions = role.Permissions, Seperated = role.Seperated }).Id);

            Console.WriteLine("Duplicating emojis...");
            foreach (var emoji in guild.Emojis)
            {
                try
                {
                    ourGuild.CreateEmoji(new EmojiProperties() { Name = emoji.Name, Image = emoji.Icon.Download() });
                }
                catch (DiscordHttpException ex)
                {
                    if (ex.Code == DiscordError.InvalidFormBody) // This is used whenever discord wants to give us parameter-specific errors
                    {
                        foreach (var param in ex.InvalidParameters)
                        {
                            foreach (var err in param.Value)
                            {
                                if (err.Code != "BINARY_TYPE_MAX_SIZE") // Bigger servers can have emojis with bigger file sizes, so let's just ignore these errors
                                    Console.WriteLine($"Error in {param.Key}. Reason: {err.Message}");
                            }
                        }
                    }
                    else if (ex.Code == DiscordError.MaximumEmojis)
                        break;
                    else
                        throw;
                }
            }

            Console.WriteLine("Removing default channels...");
            foreach (var channel in ourGuild.GetChannels())
                channel.Delete();

            Console.WriteLine("Duplicating channels...");
            Dictionary<ulong, ulong> dupedCategories = new Dictionary<ulong, ulong>();
            foreach (var channel in guild.Channels.OrderBy(c => c.Type != ChannelType.Category))
            {
                var ourChannel = ourGuild.CreateChannel(channel.Name, channel.IsText ? ChannelType.Text : channel.Type, channel.ParentId.HasValue ? dupedCategories[channel.ParentId.Value] : (ulong?)null);
                ourChannel.Modify(new GuildChannelProperties() { Position = channel.Position });

                if (ourChannel.Type == ChannelType.Text)
                {
                    var channelAsText = (TextChannel)channel;

                    ((TextChannel)ourChannel).Modify(new TextChannelProperties() { Nsfw = channelAsText.Nsfw, SlowMode = channelAsText.SlowMode, Topic = channelAsText.Topic });
                }
                else if (ourChannel.Type == ChannelType.Voice)
                {
                    var channelAsVoice = (VoiceChannel)channel;

                    ((VoiceChannel)ourChannel).Modify(new VoiceChannelProperties() { Bitrate = Math.Max(96000, channelAsVoice.Bitrate), UserLimit = channelAsVoice.UserLimit });
                }

                foreach (var overwrite in channel.PermissionOverwrites)
                {
                    if (overwrite.Type == PermissionOverwriteType.Role)
                        ourChannel.AddPermissionOverwrite(dupedRoles[overwrite.AffectedId], PermissionOverwriteType.Role, overwrite.Allow, overwrite.Deny);
                }

                if (ourChannel.Type == ChannelType.Category)
                    dupedCategories.Add(channel.Id, ourChannel.Id);
            }

            Console.WriteLine("Done");
        }
    }
}