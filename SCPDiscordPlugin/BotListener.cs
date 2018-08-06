﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SCPDiscord
{
    class BotListener
    {
        SCPDiscordPlugin plugin;
        public BotListener(SCPDiscordPlugin plugin)
        {
            this.plugin = plugin;
            while (true)
            {
                //Listen for connections
                if (plugin.clientSocket.Connected)
                {
                    try
                    {
                        //Discord messages can be up to 2000 chars long, UTF8 chars can be up to 4 bytes long.
                        byte[] data = new byte[8000];

                        NetworkStream stream = plugin.clientSocket.GetStream();

                        int lengthOfData = stream.Read(data, 0, data.Length);

                        string incomingData = System.Text.Encoding.UTF8.GetString(data, 0, lengthOfData);

                        List<string> messages = new List<string>(incomingData.Split('\n'));

                        //If several messages come in at the same time, process all of them
                        while(messages.Count > 0)
                        {
                            string discordMessage = messages[0];
                            string[] args = discordMessage.Split(' ');

                            //A verification that message is a command and not some left over string in the socket
                            if (args[0] == "command")
                            {
                                if (args[1] == "ban")
                                {
                                    //Check if the command has enough arguments
                                    if (args.Length >= 4)
                                    {
                                        BanCommand(args[2], args[3], MergeBanReason(args));
                                    }
                                    else
                                    {
                                        Dictionary<string, string> variables = new Dictionary<string, string>
                                        {
                                            { "command", discordMessage }
                                        };
                                        plugin.SendParsedMessageAsync("default", "botresponses.missingarguments", variables);
                                    }
                                }
                                else if (args[1] == "kick")
                                {
                                    //Check if the command has enough arguments
                                    if (args.Length >= 3)
                                    {
                                        KickCommand(args[2]);
                                    }
                                    else
                                    {
                                        Dictionary<string, string> variables = new Dictionary<string, string>
                                        {
                                            { "command", discordMessage }
                                        };
                                        plugin.SendParsedMessageAsync("default", "botresponses.missingarguments", variables);
                                    }
                                }
                                else if (args[1] == "unban")
                                {
                                    //Check if the command has enough arguments
                                    if (args.Length >= 3)
                                    {
                                        UnbanCommand(args[2]);
                                    }
                                    else
                                    {
                                        Dictionary<string, string> variables = new Dictionary<string, string>
                                        {
                                            { "command", discordMessage }
                                        };
                                        plugin.SendParsedMessageAsync("default", "botresponses.missingarguments", variables);
                                    }
                                }
                                plugin.Info("From discord: " + discordMessage);
                            }
                            messages.RemoveAt(0);
                        }
                    }
                    catch (Exception ex)
                    {
                        plugin.Debug(ex.ToString());
                        plugin.clientSocket.Close();
                    }
                }
                else
                {
                    Thread.Sleep(200);
                }
            }
        }

        /// <summary>
        /// Handles a ban command from Discord.
        /// </summary>
        /// <param name="steamID">SteamID of player to be banned.</param>
        /// <param name="duration">Duration of ban expressed as xu where x is a number and u is a character representing a unit of time.</param>
        /// <param name="reason">Optional reason for the ban.</param>
        private void BanCommand(string steamID, string duration, string reason = "")
        {
            // Perform very basic SteamID validation.
            if (!IsPossibleSteamID(steamID))
            {
                Dictionary<string, string> variables = new Dictionary<string, string>
                {
                    { "steamid", steamID }
                };
                plugin.SendParsedMessageAsync("default", "botresponses.invalidsteamid", variables);
                return;
            }

            // Create duration timestamp.
            string humanReadableDuration = "";
            DateTime endTime = ParseBanDuration(duration, ref humanReadableDuration);
            if (endTime == DateTime.MinValue)
            {
                Dictionary<string, string> variables = new Dictionary<string, string>
                {
                    { "duration", duration }
                };
                plugin.SendParsedMessageAsync("default", "botresponses.invalidduration", variables);
                return;
            }

            string name = "";
            if(!plugin.GetPlayerName(steamID, ref name))
            {
                name = "Offline player";
            }

            // Add the player to the SteamIDBans file.
            StreamWriter streamWriter = new StreamWriter(FileManager.AppFolder + "/SteamIdBans.txt", true);
            streamWriter.WriteLine(name + ';' + steamID + ';' + endTime.Ticks + ';' + reason + ";DISCORD;" + DateTime.UtcNow.Ticks);
            streamWriter.Dispose();

            // Kicks the player if they are online.
            plugin.KickPlayer(steamID, "Banned for the following reason: '" + reason + "'");

            Dictionary<string, string> banVars = new Dictionary<string, string>
                {
                    { "name",       name                    },
                    { "steamid",    steamID                 },
                    { "reason",     reason                  },
                    { "duration",   humanReadableDuration   }
                };
            plugin.SendParsedMessageAsync("default", "botresponses.playerbanned", banVars);
        }

        /// <summary>
        /// Handles an unban command from Discord.
        /// </summary>
        /// <param name="steamID">SteamID of player to be unbanned.</param>
        private void UnbanCommand(string steamID)
        {
            // Perform very basic SteamID validation. (Also secretly maybe works on ip addresses now)
            if (!IsPossibleSteamID(steamID) && !IPAddress.TryParse(steamID, out IPAddress address))
            {
                Dictionary<string, string> variables = new Dictionary<string, string>
                {
                    { "steamidorip", steamID }
                };
                plugin.SendParsedMessageAsync("default", "botresponses.invalidsteamidorip", variables);
                return;
            }

            // Read and save all lines to file except for the one to be unbanned
            File.WriteAllLines(FileManager.AppFolder + "/SteamIdBans.txt", File.ReadAllLines(FileManager.AppFolder + "/SteamIdBans.txt").Where(w => !w.Contains(steamID)).ToArray());

            // Read and save all lines to file except for the one to be unbanned
            File.WriteAllLines(FileManager.AppFolder + "/IpBans.txt", File.ReadAllLines(FileManager.AppFolder + "/IpBans.txt").Where(w => !w.Contains(steamID)).ToArray());

            Dictionary<string, string> unbanVars = new Dictionary<string, string>
            {
                { "steamidorip", steamID }
            };
            plugin.SendParsedMessageAsync("default", "botresponses.playerunbanned", unbanVars);
        }

        /// <summary>
        /// Handles the kick command.
        /// </summary>
        /// <param name="steamID">SteamID of player to be kicked.</param>
        private void KickCommand(string steamID)
        {
            //Perform very basic SteamID validation
            if (!IsPossibleSteamID(steamID))
            {
                Dictionary<string, string> variables = new Dictionary<string, string>
                {
                    { "steamid", steamID }
                };
                plugin.SendParsedMessageAsync("default", "botresponses.invalidsteamid", variables);
                return;
            }

            //Get player name for feedback message
            string playerName = "";
            plugin.GetPlayerName(steamID, ref playerName);

            //Kicks the player
            if (plugin.KickPlayer(steamID))
            {
                Dictionary<string, string> variables = new Dictionary<string, string>
                {
                    { "name", playerName },
                    { "steamid", steamID }
                };
                plugin.SendParsedMessageAsync("default", "botresponses.playerkicked", variables);
            }
            else
            {
                Dictionary<string, string> variables = new Dictionary<string, string>
                {
                    { "steamid", steamID }
                };
                plugin.SendParsedMessageAsync("default", "botresponses.playernotfound", variables);
            }
        }
       
        /// <summary>
        /// Merges the words of the ban reason to one string.
        /// </summary>
        /// <param name="args">The entire command split into words.</param>
        /// <returns>The resulting string, empty string if no reason was given.</returns>
        private string MergeBanReason(string[] args)
        {
            string output = "";
            for(int i = 4; i < args.Length; i++)
            {
                output += args[i];
                output += ' ';
            }
            while(output.Length > 0 && output.EndsWith(" "))
            {
                output = output.Remove(output.Length -1);
            }
            return output;
        }

        /// <summary>
        /// Returns a timestamp of the duration's end, and outputs a human readable duration for command feedback.
        /// </summary>
        /// <param name="duration">Duration of ban in format 'xu' where x is a number and u is a character representing a unit of time.</param>
        /// <param name="humanReadableDuration">String to be filled by the function with the duration in human readable form.</param>
        /// <returns>Returns a timestamp of the duration's end.</returns>
        private DateTime ParseBanDuration(string duration, ref string humanReadableDuration)
        {
            //Check if the amount is a number
            if (!int.TryParse(new string(duration.Where(Char.IsDigit).ToArray()), out int amount))
            {
                return DateTime.MinValue;
            }

            char unit = duration.Where(Char.IsLetter).ToArray()[0];
            TimeSpan timeSpanDuration = new TimeSpan();
            
            // Parse time into a TimeSpan duration and string
            if (unit == 's')
            {
                humanReadableDuration = amount + " second";
                timeSpanDuration = new TimeSpan(0, 0, 0, amount);
            }
            else if (unit == 'm')
            {
                humanReadableDuration = amount + " minute";
                timeSpanDuration = new TimeSpan(0,0,amount,0);
            }
            else if (unit == 'h')
            {
                humanReadableDuration = amount + " hour";
                timeSpanDuration = new TimeSpan(0, amount, 0, 0);
            }
            else if (unit == 'd')
            {
                humanReadableDuration = amount + " day";
                timeSpanDuration = new TimeSpan(amount, 0, 0, 0);
            }
            else if (unit == 'w')
            {
                humanReadableDuration = amount + " week";
                timeSpanDuration = new TimeSpan(7 * amount, 0, 0, 0);
            }
            else if (unit == 'M')
            {
                humanReadableDuration = amount + " month";
                timeSpanDuration = new TimeSpan(30 * amount, 0, 0, 0);
            }
            else if (unit == 'y')
            {
                humanReadableDuration = amount + " year";
                timeSpanDuration = new TimeSpan(365 * amount, 0, 0, 0);
            }

            // Pluralize string if needed
            if (amount != 1)
            {
                humanReadableDuration += 's';
            }

            return DateTime.UtcNow.Add(timeSpanDuration);
        }

        /// <summary>
        /// Does very basic validation of a SteamID.
        /// </summary>
        /// <param name="steamID">A SteamID.</param>
        /// <returns>True if a possible SteamID, false if not.</returns>
        private bool IsPossibleSteamID(string steamID)
        {
            return (steamID.Length == 17 && long.TryParse(steamID, out long n));
        }
    }
}
