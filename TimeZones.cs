using Oxide.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using Oxide.Game.Rust.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Time Zones", "Misstake", "0.4.0")]
    [Description("Sets time of day depending on the zone you're in (with ZoneManager)")]

    public class TimeZones : CovalencePlugin
    {
        [PluginReference("ZoneManager")]
        private Plugin ZoneManager;
        public const string PermissionName = "timezones.admin";

        #region PlayerData

        public class PlayerData
        {
            public bool TimeZoneActive { get; set; }
            public bool TimeZoneDisabled { get; set; }
        }

        public static PlayerData GetPlayerData(string Id)
        {
            PlayerData data;
            if (!PData.TryGetValue(Id, out data))
            {
                data = new PlayerData();
                PData.Add(Id, data);
            }
            return data;
        }

        private static Dictionary<string, PlayerData> PData { get; set; } = new Dictionary<string, PlayerData>();

        #endregion

        #region ZoneData

        public class ZoneData
        {
            public Dictionary<string, ZoneInfo> Zones = new Dictionary<string, ZoneInfo>();
        }

        public class ZoneInfo
        {
            public float TimeToSet { get; set; } = 15.0f;
        }

        private DynamicConfigFile _zoneFile;

        public ZoneData ZData;
        #endregion

        #region Hooks

        void Loaded()
        {
            if (ZoneManager == null)
            {
                PrintError("ZoneManager is required for this plugin to run, get it at https://umod.org/plugins/zone-manager");
                Unsubscribe(nameof(OnServerSave));
                Unsubscribe(nameof(OnExitZone));
                Unsubscribe(nameof(OnEnterZone));
                Unsubscribe(nameof(OnPlayerInit));
            }
        }

        void Init()
        {
            if(!permission.PermissionExists(PermissionName, this))
                permission.RegisterPermission(PermissionName, this);
            Unsubscribe("CanNetworkTo");
            _zoneFile = Interface.Oxide.DataFileSystem.GetFile("TimeZones");
            LoadData();
        }

        void OnServerInitialized()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList.ToList())
            {
                OnPlayerInit(player);
            }
        }

        void OnServerSave()
        {
            SaveData();
        }

        void Unload()
        {
            SaveData();
        }

        void OnPlayerInit(BasePlayer player)
        {
            var returnCall = ZoneManager?.Call("GetPlayerZoneIDs", player) as string[];

            if (returnCall != null)
            {
                foreach (var zoneId in returnCall)
                {
                    OnEnterZone(zoneId, player);
                }
            }
        }

        private void OnEnterZone(string zoneId, BasePlayer player)
        {
            PlayerData pData = GetPlayerData(player.UserIDString);
            if (pData.TimeZoneDisabled)
            {
                return;
            }

            ZoneInfo zone;
            if (ZData.Zones.TryGetValue(zoneId, out zone))
            {
                pData.TimeZoneActive = true;
                LockPlayerTime(player, zone.TimeToSet);
            }
        }

        private void OnExitZone(string zoneId, BasePlayer player)
        {
            PlayerData data = GetPlayerData(player.UserIDString);
            if (data.TimeZoneDisabled)
            {
                return;
            }

            if (ZData.Zones.ContainsKey(zoneId))
            {
                UnlockPlayerTime(player);
                data.TimeZoneActive = false;
            }
        }

        #endregion

        #region Chat Commands

        [Command("timezone"), Permission("timezones.admin")]
        void TimeZoneCommand(IPlayer player, string command, string[] args)
        {
            //I know, not necessary, but just in case left it in here
            if (!player.HasPermission(PermissionName))
            {
                player.Reply(Msg("NoPermission"), player.Id, player.IsServer);
                return;
            }
            if (args.Length == 0)
            {
                player.Reply(Msg("SyntaxTimeZone", player.Id, player.IsServer));
                return;
            }
            switch (args[0].ToLower())
            {
                case "toggle":
                    ToggleTimeZone(player, args);
                    return;
                case "set":
                    SetTimeZone(player, args);
                    return;
                case "disable":
                    RemoveTimeZone(player, args);
                    return;
                case "list":
                    ListTimeZones(player, args);
                    return;
                case "help":
                    player.Reply(player.IsServer
                        ? Msg("HelpTimeZoneConsole", player.Id, player.IsServer)
                        : Msg("HelpTimeZone", player.Id, player.IsServer));
                    return;
                case "default":
                    player.Reply(Msg("SyntaxTimeZone", player.Id, player.IsServer));
                    return;
            }

        }

        #endregion

        #region HelperFunctions

        private void ToggleTimeZone(IPlayer player, string[] args)
        {
            IPlayer target = player;
            if (args.Length > 1)
            {
                target = covalence.Players.FindPlayer(args[1]);
                if (target == null)
                {
                    player.Reply(Msg("NoPlayerFound", player.Id, player.IsServer));
                    return;
                }
            }

            BasePlayer basePlayer = target.Object as BasePlayer;
            if (basePlayer == null)
            {
                player.Reply(Msg("CantToggleNonPlayer", player.Id, player.IsServer));
            }

            PlayerData data = GetPlayerData(target.Id);
            data.TimeZoneDisabled = !data.TimeZoneDisabled;

            if (data.TimeZoneDisabled)
            {
                UnlockPlayerTime(basePlayer);
                player.Reply(Msg("TimeZoneDeactivated", player.Id, player.IsServer));
            }
            else
            {
                player.Reply(Msg("TimeZoneActivated", player.Id, player.IsServer));
            }
        }

        private void SetTimeZone(IPlayer player, string[] args)
        {
            if (args.Length != 3)
            {
                player.Reply(Msg("SyntaxTimeZone", player.Id, player.IsServer));
                return;
            }

            if (args[2] != "day" && args[2] != "night")
            {
                player.Reply(Msg("InvalidDayOrNight", player.Id, player.IsServer));
                return;
            }

            bool day = args[2] == "day";
            int n;
            if (!int.TryParse(args[1], out n))
            {
                player.Reply(Msg("InvalidZoneID", player.Id, player.IsServer));
                return;
            }

            if (ZoneManager?.Call("CheckZoneID", n.ToString()) == null)
            {
                player.Reply(Msg("ZoneNotFound", player.Id, player.IsServer));
                return;
            }
            ZoneInfo zoneInfo;
            if (!ZData.Zones.TryGetValue(n.ToString(), out zoneInfo))
            {
                zoneInfo = new ZoneInfo();
                ZData.Zones.Add(n.ToString(), zoneInfo);
            }

            if (day)
            {
                zoneInfo.TimeToSet = 15f;
            }
            else
            {
                zoneInfo.TimeToSet = 1f;

            }
            player.Reply(String.Format(Msg("ZoneSet", player.Id, player.IsServer), n.ToString(), day ? "day" : "night"));

        }

        private void RemoveTimeZone(IPlayer player, string[] args)
        {
            if (args.Length != 2)
            {
                player.Reply(Msg("SyntaxTimeZone", player.Id, player.IsServer));
                return;
            }

            int n;
            if (!int.TryParse(args[1], out n))
            {
                player.Reply(Msg("InvalidZoneID", player.Id, player.IsServer));
                return;
            }

            ZData.Zones.Remove(n.ToString());
            player.Reply(String.Format(Msg("ZoneRemoved", player.Id, player.IsServer), n.ToString()));
        }

        private void ListTimeZones(IPlayer player, string[] args)
        {
            StringBuilder sb = new StringBuilder(Msg("ZoneList", player.Id, player.IsServer));
            foreach (var zone in ZData.Zones)
            {
                sb.AppendLine($"ZoneID: {zone.Key}, Time: {zone.Value.TimeToSet.ToString()}");
            }

            player.Reply(sb.ToString());
        }

        private void SaveData()
        {
            _zoneFile.WriteObject(ZData);
        }

        private void LoadData()
        {
            try
            {
                ZData = _zoneFile.ReadObject<ZoneData>();
                Puts("Loading data.");

            }
            catch
            {
                Puts("Couldn't load player data, creating new datafile");
                ZData = new ZoneData();
            }
            if (ZData == null)
            {
                Puts("Couldn't load player data, creating new datafile");
                ZData = new ZoneData();
            }
        }

        #endregion

        #region NightVision Plugin API 1.3.0

        [PluginReference("NightVision")]
        RustPlugin NightVisionRef;

        public void LockPlayerTime(BasePlayer player, float time, float fog = -1, float rain = -1)
        {
            var args = Core.ArrayPool.Get(4);
            args[0] = player;
            args[1] = time;
            args[2] = fog;
            args[3] = rain;
            NightVisionRef?.CallHook("LockPlayerTime", args);
            Core.ArrayPool.Free(args);
        }

        public void UnlockPlayerTime(BasePlayer player)
        {
            var args = Core.ArrayPool.Get(1);
            args[0] = player;
            NightVisionRef?.CallHook("UnlockPlayerTime", args);
            Core.ArrayPool.Free(args);
        }

        #endregion

        #region Lang API

        /* Since most RCON consoles don't support markup I added a Connection == null check with a regex to remove the markup */
        string Msg(string key, string Id = null, bool isServer = false)
        {
            string message = lang.GetMessage(key, this, Id);
            if (isServer)
            {
                message = Regex.Replace(message, "<[^>]*>", "");
            }

            return message;
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>{
                { "TimeZoneActivated", "TimeZones activated" },
                { "TimeZoneDeactivated", "TimeZones deactivated" },
                { "NoPermission","You do not have permission to use this command."},
                {
                    "HelpTimeZone", "<size=18><color=red>TimeZones</color></size>\n" +
                                    " Available commands are:\n" +
                                    "<color=#fda60a>/timezone toggle [player]</color> - toggle all timezones on or off for specified player \n" +
                                    "<color=#fda60a>/timezone set (zone ID) (day/night)</color> - Sets the specified zone as a timezone.\n" +
                                    "<color=#fda60a>/timezone disable (zone ID)</color> - Disables the given timezone.\n" +
                                    "<color=#fda60a>/timezone list</color> - Gives a list of available timeZones and their times\n" },
                {
                    "HelpTimeZoneConsole", "<color=red>TimeZones</color>\n" +
                                    " Available console commands are:\n" +
                                    "<color=#fda60a>timezone toggle [player]</color> - toggle all timezones on or off for specified player \n" +
                                    "<color=#fda60a>timezone set (zone ID) (day/night)</color> - Sets the specified zone as a timezone.\n" +
                                    "<color=#fda60a>timezone disable (zone ID)</color> - Disables the given timezone.\n" +
                                    "<color=#fda60a>/timezone list</color> - Gives a list of available timeZones and their times\n" },
                { "SyntaxTimeZone", "<color=red>Invalid Syntax</color> use <color=#fda60a>/timezone help</color> to get a list of available commands" },
                { "SyntaxTimeZoneConsole", "<color=red>Invalid Syntax</color> use <color=#fda60a>timezone help</color> to get a list of available commands" },
                { "ZoneNotFoundColor", "<color=red>Zone not found</color> The given zone doesn't exist, please check the given zone ID." },
                { "ZoneSet", "<color=#33ff33>Succes:</color> set the zone with ID {0} as a {1} zone." },
                { "InvalidZoneID", "<color=red>Invalid Zone ID</color> please provide a valid zone ID containing only numbers." },
                { "ZoneRemoved", "<color=#33ff33>Succes</color>: disabled timezone with ID {0}" },
                { "InvalidDayOrNight", "<color=red>Invalid time</color> please provide a day or night value." },
                {"NoPlayerFound", "No player found with given name." },
                {"CantToggleNonPlayer", "You can't toggle the timezones for a non-player, specify a player name." },
                {"ZoneList", "Available zones are: \n" }
            }, this);
        }

        #endregion

    }
}