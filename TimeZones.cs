
using Facepunch;
using Network;
using Oxide.Core;
using ProtoBuf;
using System;
using System.Collections.Generic;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using Debug = UnityEngine.Debug;

namespace Oxide.Plugins
{
    [Info("Time Zones", "Misstake", "0.2.2")]
    [Description("Sets time of day depending on the zone you're in (with ZoneManager)")]

    public class TimeZones : RustPlugin
    {
        [PluginReference]
        Plugin ZoneManager;
        public static string PermissionName = "timezones.admin";

        public Timer Timer { get; set; }

        #region PlayerData

        public class PlayerData
        {
            public bool TimeZoneActive { get; set; }
            public bool TimeZoneDisabled { get; set; }
            public DateTime TimeToSet { get; set; }
        }

        public static PlayerData GetPlayerData(BasePlayer player)
        {
            PlayerData data;
            if (!PData.TryGetValue(player.userID, out data))
            {
                data = new PlayerData();
                PData.Add(player.userID, data);
            }
            return data;
        }

        private static Dictionary<ulong, PlayerData> PData { get; set; } = new Dictionary<ulong, PlayerData>();

        #endregion

        #region ZoneData

        public class ZoneData
        {
            public Dictionary<string, ZoneInfo> Zones = new Dictionary<string, ZoneInfo>();
        }

        public class ZoneInfo
        {
            public DateTime TimeToSet { get; set; } = new DateTime(2018, 1, 31, 15, 0, 0);
        }

        private DynamicConfigFile _zoneFile;

        public ZoneData ZData;
        #endregion

        #region Hooks

        public static TimeZones Plugin { get; set; }

        void Loaded()
        {
            if (ZoneManager == null)
            {
                PrintError("ZoneManager is required for this plugin to run, get it at https://umod.org/plugins/zone-manager");
                Unsubscribe(nameof(OnServerSave));
                Unsubscribe(nameof(OnExitZone));
                Unsubscribe(nameof(OnEnterZone));
                Unsubscribe(nameof(CanNetworkTo));
                Unsubscribe(nameof(OnPlayerInit));
            }
        }

        void Init()
        {
            Plugin = this;
            permission.RegisterPermission(PermissionName, this);
            lang.RegisterMessages(lang_en, this);
            Unsubscribe("CanNetworkTo");
            _zoneFile = Interface.Oxide.DataFileSystem.GetFile("TimeZones");
            LoadData();
            CheckData();
            CheckSubscriptions();
        }

        void OnServerInitialized()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                OnPlayerInit(player);
            }
        }

        void OnServerSave()
        {
            CheckData();
            CheckSubscriptions();
            SaveData();
        }

        void Unload()
        {
            SaveData();
        }

        void OnPlayerDisconnected(BasePlayer player)
        {
            PData.Remove(player.userID);
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
            PlayerData pData = GetPlayerData(player);
            if (pData.TimeZoneDisabled)
            {
                return;
            }

            ZoneInfo zone;
            if (ZData.Zones.TryGetValue(zoneId, out zone))
            {
                pData.TimeZoneActive = true;
                pData.TimeToSet = zone.TimeToSet;
                CheckSubscriptions();
            }
        }

        private void OnExitZone(string zoneId, BasePlayer player)
        {
            PlayerData data = GetPlayerData(player);
            if (data.TimeZoneDisabled)
            {
                return;
            }

            if (ZData.Zones.ContainsKey(zoneId))
            {
                data.TimeZoneActive = false;
                PData.Remove(player.userID);
                CheckSubscriptions();
            }
        }

        /*
         * Author: JakeRich
         * Code based on the same function in JakeRich's plugin Nightvision:
         * https://umod.org/plugins/night-vision
         *
         * Added some optimization and changes.
         */
        object CanNetworkTo(BaseNetworkable entity, BasePlayer player)
        {
            if (!(entity is EnvSync))
            {
                return null;
            }

            PlayerData data;
            if (!PData.TryGetValue(player.userID, out data))
            {
                return null;
            }
            
            if (!data.TimeZoneActive)
            {
                return null;
            }

            var env = (EnvSync) entity;
            if (Net.sv.write.Start())
            {
                Connection connection = player.net.connection;
                ++connection.validate.entityUpdates;
                BaseNetworkable.SaveInfo saveInfo = new global::BaseNetworkable.SaveInfo
                {
                    forConnection = player.net.connection,
                    forDisk = false
                };
                Net.sv.write.PacketID(Message.Type.Entities);
                Net.sv.write.UInt32(player.net.connection.validate.entityUpdates);
                using (saveInfo.msg = Pool.Get<Entity>())
                {
                    env.Save(saveInfo);
                    if (saveInfo.msg.baseEntity == null)
                    {
                        Debug.LogError(this + ": ToStream - no BaseEntity!?");
                    }
                    saveInfo.msg.environment.dateTime = data.TimeToSet.ToBinary();
                    saveInfo.msg.environment.fog = 0;
                    saveInfo.msg.environment.rain = 0;
                    saveInfo.msg.environment.clouds = 0;
                    if (saveInfo.msg.baseNetworkable == null)
                    {
                        Debug.LogError(this + ": ToStream - no baseNetworkable!?");
                    }
                    saveInfo.msg.ToProto(Net.sv.write);
                    env.PostSave(saveInfo);
                    Net.sv.write.Send(new SendInfo(player.net.connection));
                }
            }

            return false;
        }

        #endregion

        #region Chat Commands

        [ChatCommand("nightvision")]
        void NightVisionCommand(BasePlayer player, string command, string[] args)
        {
            BasePlayer target = player;
            if (args.Length != 0)
            {
                target = RustCore.FindPlayer(args[0]) ?? player;
            }
            if (!permission.UserHasPermission(player.UserIDString, PermissionName))
            {
                PrintToChat(player, string.Format(lang.GetMessage("AdminsOnly", Plugin, player.UserIDString), PermissionName));
                return;
            }

            PlayerData data = GetPlayerData(target);
            
            if (!permission.UserHasPermission(target.UserIDString, PermissionName))
            {
                PData.Remove(player.userID);
                CheckSubscriptions();
                return;
            }

            data.TimeZoneActive = !data.TimeZoneActive;

            if (data.TimeZoneActive)
            {
                data.TimeToSet = new DateTime(2018, 1, 31, 15, 0, 0);
                data.TimeZoneDisabled = true;
                PrintToChat(player, lang.GetMessage("Activated", Plugin, player.UserIDString));
            }
            else
            {
                data.TimeZoneDisabled = false;
                PData.Remove(player.userID);
                CheckSubscriptions();
                PrintToChat(player, lang.GetMessage("Deactivated", Plugin, player.UserIDString));
            }
        }

        [ChatCommand("timezone")]
        void TimeZoneCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionName))
            {
                return;
            }
            if (args.Length == 0)
            {
                SendReply(player, lang.GetMessage("SyntaxTimeZone", this, player.UserIDString));
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
                case "help":
                    SendReply(player, lang.GetMessage("HelpTimeZone", this, player.UserIDString));
                    return;
                case "default":
                    SendReply(player, lang.GetMessage("SyntaxTimeZone", this, player.UserIDString));
                    return;
            }

        }

        [ConsoleCommand("timezone")]
        void TimeZoneCCommand(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin && !permission.UserHasPermission(arg.Connection?.userid.ToString(), "timezones.admin"))
                return;

            if (arg.Args == null || arg.Args.Length == 0)
            {
                SendReply(arg, lang.GetMessage("SyntaxTimeZoneConsole", this));
                return;
            }

            switch (arg.Args[0].ToLower())
            {
                case "toggle":
                    ToggleTimeZone(arg);
                    return;
                case "set":
                    SetTimeZone(arg);
                    return;
                case "disable":
                    RemoveTimeZone(arg);
                    return;
                case "help":
                    SendReply(arg, lang.GetMessage("HelpTimeZoneConsole", this));
                    return;
                case "default":
                    SendReply(arg, lang.GetMessage("SyntaxTimeZoneConsole", this));
                    return;
            }
        }

        #endregion

        #region HelperFunctions

        private void ToggleTimeZone(BasePlayer player, string[] args)
        {
            BasePlayer target = player;
            if (args.Length > 1)
            {
                target = RustCore.FindPlayer(args[0]) ?? player;
            }

            if (!permission.UserHasPermission(player.UserIDString, PermissionName))
            {
                PrintToChat(player,
                    string.Format(lang.GetMessage("AdminsOnly", Plugin, player.UserIDString), PermissionName));
                return;
            }

            PlayerData data = GetPlayerData(target);
            data.TimeZoneDisabled = !data.TimeZoneDisabled;

            if (data.TimeZoneDisabled)
            {
                data.TimeToSet = new DateTime(2018, 1, 31, 15, 0, 0);
                PrintToChat(player, lang.GetMessage("TimeZoneDeactivated", Plugin, player.UserIDString));
            }
            else
            {
                PrintToChat(player, lang.GetMessage("TimeZoneActivated", Plugin, player.UserIDString));
                PData.Remove(player.userID);
            }
        }

        private void ToggleTimeZone(ConsoleSystem.Arg arg)
        {
            if (arg.Args.Length != 2)
            {
                SendReply(arg, lang.GetMessage("SyntaxTimeZoneConsole", this));
            }

            BasePlayer target = RustCore.FindPlayer(arg.Args[1]);
            if (target == null)
            {
                SendReply(arg, lang.GetMessage("NoPlayerFound", this));
                return;
            }

            PlayerData data = GetPlayerData(target);
            data.TimeZoneDisabled = !data.TimeZoneDisabled;

            if (data.TimeZoneDisabled)
            {;
                SendReply(arg, lang.GetMessage("TimeZoneDeactivated", this));
            }
            else
            {
                SendReply(arg, lang.GetMessage("TimeZoneActivated", this));
                PData.Remove(target.userID);
            }
        }

        private void SetTimeZone(ConsoleSystem.Arg arg)
        {
            if (arg.Args.Length != 3)
            {
                SendReply(arg, lang.GetMessage("SyntaxTimeZone", this));
                return;
            }

            if (arg.Args[2] != "day" && arg.Args[2] != "night")
            {
                SendReply(arg, lang.GetMessage("InvalidDayOrNight", this));
                return;
            }

            bool day = (arg.Args[2] == "day") ? true : false;
            int n;
            if (!int.TryParse(arg.Args[1], out n))
            {
                SendReply(arg, lang.GetMessage("InvalidZoneID", this));
                return;
            }

            if (ZoneManager?.Call("CheckZoneID", n.ToString()) == null)
            {
                SendReply(arg, lang.GetMessage("ZoneNotFound", this));
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
                zoneInfo.TimeToSet = new DateTime(2018, 1, 31, 15, 0, 0);
            }
            else
            {
                zoneInfo.TimeToSet = new DateTime(2018, 1, 31, 1, 0, 0);

            }
            SendReply(arg, String.Format(lang.GetMessage("ZoneSet", this), n.ToString(), day ? "day" : "night"));

        }

        private void SetTimeZone(BasePlayer player, string[] args)
        {
            if (args.Length != 3)
            {
                SendReply(player, lang.GetMessage("SyntaxTimeZone", this, player.UserIDString));
                return;
            }

            if (args[2] != "day" && args[2] != "night")
            {
                SendReply(player, lang.GetMessage("InvalidDayOrNight", this, player.UserIDString));
                return;
            }

            bool day = (args[2] == "day") ? true : false;
            int n;
            if (!int.TryParse(args[1], out n))
            {
                SendReply(player, lang.GetMessage("InvalidZoneID", this, player.UserIDString));
                return;
            }

            if (ZoneManager?.Call("CheckZoneID", n.ToString()) == null)
            {
                SendReply(player, lang.GetMessage("ZoneNotFound", this, player.UserIDString));
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
                zoneInfo.TimeToSet = new DateTime(2018, 1, 31, 15, 0, 0);
            }
            else
            {
                zoneInfo.TimeToSet = new DateTime(2018, 1, 31, 1, 0, 0);
                
            }
            SendReply(player, String.Format(lang.GetMessage("ZoneSet", this, player.UserIDString), n.ToString(), day ? "day" : "night"));

        }

        private void RemoveTimeZone(BasePlayer player, string[] args)
        {
            if (args.Length != 2)
            {
                SendReply(player, lang.GetMessage("SyntaxTimeZone", this, player.UserIDString));
                return;
            }

            int n;
            if (!int.TryParse(args[1], out n))
            {
                SendReply(player, lang.GetMessage("InvalidZoneID", this, player.UserIDString));
                return;
            }

            ZData.Zones.Remove(n.ToString());
            SendReply(player, String.Format(lang.GetMessage("ZoneRemoved", this, player.UserIDString), n.ToString()));
        }

        private void RemoveTimeZone(ConsoleSystem.Arg arg)
        {
            if (arg.Args.Length != 2)
            {
                SendReply(arg, lang.GetMessage("SyntaxTimeZone", this));
                return;
            }

            int n;
            if (!int.TryParse(arg.Args[1], out n))
            {
                SendReply(arg, lang.GetMessage("InvalidZoneID", this));
                return;
            }

            ZData.Zones.Remove(n.ToString());
            SendReply(arg, String.Format(lang.GetMessage("ZoneRemoved", this), n.ToString()));
        }


        private void CheckSubscriptions()
        {
            //Check if a subscription to the CanNetworkTo hook is still necessary
            if (PData.Count == 0)
            {
                Unsubscribe(nameof(CanNetworkTo));
            }
            else
            {
                Subscribe(nameof(CanNetworkTo));
            }
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

        private void CheckData()
        {
            //Check each entry in the playerData and if it's not necessary anymore remove it
            foreach (var player in PData)
            {
                if (!player.Value.TimeZoneDisabled && !player.Value.TimeZoneActive)
                    PData.Remove(player.Key);
            }
        }
        #endregion


        #region Lang API

        public Dictionary<string, string> lang_en = new Dictionary<string, string>()
        {
            {"Activated","Night vision activated"},
            {"Deactivated","Night vision deactivated"},
            {"TimeZoneActivated", "TimeZones activated" },
            {"TimeZoneDeactivated", "TimeZones deactivated" },
            {"AdminsOnly","This command is only for people with the permission \"{0}\"!"},
            {"HelpTimeZone", "<size=18><color=red>TimeZones</color></size>\n" +
                             " Available commands are:\n" +
                             "<color=#fda60a>/timezone toggle [player]</color> - toggle all timezones on or off for specified player \n" +
                             "<color=#fda60a>/timezone set (zone ID) (day/night)</color> - Sets the specified zone as a timezone.\n" +
                             "<color=#fda60a>/timezone disable (zone ID)</color> - Disables the given timezone." },
            {"HelpTimeZoneConsole", "<color=red>TimeZones</color>\n" +
                             " Available console commands are:\n" +
                             "<color=#fda60a>timezone toggle [player]</color> - toggle all timezones on or off for specified player \n" +
                             "<color=#fda60a>timezone set (zone ID) (day/night)</color> - Sets the specified zone as a timezone.\n" +
                             "<color=#fda60a>timezone disable (zone ID)</color> - Disables the given timezone." },
            {"SyntaxTimeZone", "<color=red>Invalid Syntax</color> use <color=#fda60a>/timezone help</color> to get a list of available commands" },
            {"SyntaxTimeZoneConsole", "<color=red>Invalid Syntax</color> use <color=#fda60a>timezone help</color> to get a list of available commands" },
            {"ZoneNotFound", "<color=red>Zone not found</color> The given zone doesn't exist, please check the given zone ID." },
            {"ZoneSet", "<color=#33ff33>Succes:</color> set the zone with ID {0} as a {1} zone." },
            {"InvalidZoneID", "<color=red>Invalid Zone ID</color> please provide a valid zone ID containing only numbers." },
            {"ZoneRemoved", "<color=#33ff33>Succes</color>: disabled timezone with ID {0}" },
            {"InvalidDayOrNight", "<color=red>Invalid time</color> please provide a day or night value." }
        };

        #endregion

    }
}