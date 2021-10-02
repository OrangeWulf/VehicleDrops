using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Plugins.VehicleDropEx;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;
using VLB;

namespace Oxide.Plugins
{
    [Info("VehicleDrops", "Bazz3l", "1.1.0")]
    [Description("Spawn vehicles by throwing down a custom vehicle supply signal.")]
    public class VehicleDrops : CovalencePlugin
    {
        #region Fields

        private const string PERM_USE = "vehicledrops.use";
        
        private const string FIRED_PREFAB = "assets/prefabs/npc/m2bradley/effects/maincannonattack.prefab";
        private const string PARACHUTE_PREFAB = "assets/prefabs/misc/parachute/parachute.prefab";
        private const string SCRAP_HELICOPTER_PREFAB = "assets/content/vehicles/scrap heli carrier/scraptransporthelicopter.prefab";
        private const string MINICOPTER_PREFAB = "assets/content/vehicles/minicopter/minicopter.entity.prefab";
        private const string CAR_PREFAB = "assets/content/vehicles/modularcar/2module_car_spawned.entity.prefab";
        private const string BOAT_PREFAB = "assets/content/vehicles/boats/rowboat/rowboat.prefab";
        private const string RHIB_PREFAB = "assets/content/vehicles/boats/rhib/rhib.prefab";

        private static readonly LayerMask CollisionLayer = LayerMask.GetMask(
            "Water", 
            "Tree", 
            "Debris", 
            "Clutter", 
            "Default", 
            "Resource", 
            "Construction", 
            "Terrain", 
            "World", 
            "Deployed");
        
        private PluginConfig _config;
        private PluginData _storage;
        
        private enum DropType
        {
            ScrapHelicopter = 0,
            Minicopter = 1,
            Boat = 2,
            Rhib = 3,
            Car = 4
        }

        #endregion
        
        #region Config

        protected override void LoadDefaultConfig() => _config = PluginConfig.DefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<PluginConfig>();

                if (_config == null)
                {
                    throw new JsonException();
                }
            }
            catch
            {
                PrintWarning("Default config loaded.");

                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        class PluginConfig
        {
            public HashSet<DropConfig> DropConfigs = new HashSet<DropConfig>();

            [JsonIgnore]
            public readonly Dictionary<string, DropConfig> DropTypesByName = new Dictionary<string, DropConfig>();
            
            [JsonIgnore]
            public readonly Dictionary<ulong, DropConfig> DropTypesBySkin = new Dictionary<ulong, DropConfig>();
            
            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig
                {
                    DropConfigs = new HashSet<DropConfig>
                    {
                        new DropConfig("Scrap", 2144547783, DropType.ScrapHelicopter),
                        new DropConfig("Mini", 2144524645, DropType.Minicopter),
                        new DropConfig("Boat", 2144555007, DropType.Boat),
                        new DropConfig("Rhib", 2144558893, DropType.Rhib),
                        new DropConfig("Car", 2144560388, DropType.Car),
                    }
                };
            }

            public void RegisterDrops()
            {
                foreach (DropConfig drop in DropConfigs)
                {
                    DropTypesByName.Add(drop.Name, drop);
                    DropTypesBySkin.Add(drop.SkinID, drop);
                }
            }
            
            public DropConfig FindDropByName(string name)
            {
                DropConfig dropConfig;

                return DropTypesByName.TryGetValue(name, out dropConfig) ? dropConfig : null;
            }
            
            public DropConfig FindDropBySkin(ulong skin)
            {
                DropConfig dropConfig;

                return DropTypesBySkin.TryGetValue(skin, out dropConfig) ? dropConfig : null;
            }
        }

        class DropConfig
        {
            public string Name;
            public float Cooldown;
            public int Limit;
            public int Tier;
            public ulong SkinID;
            public DropType DropType;

            public DropConfig(string name, ulong skinID, DropType dropType)
            {
                Name = name;
                DropType = dropType;
                SkinID = skinID;
                Tier = 1;
                Limit = 5;
                Cooldown = 600f;
            }

            public bool CreateSupply(BasePlayer player)
            {
                Item item = ItemManager.CreateByName("supply.signal", 1, SkinID);

                if (item == null) return false;

                item.name = Name;
                item.MarkDirty();
                
                player.GiveItem(item);

                return true;
            }
            
            public void CreateEntity(Vector3 position)
            {
                switch (DropType)
                {
                    case DropType.ScrapHelicopter:
                        CreateEntity<MiniCopter>(SCRAP_HELICOPTER_PREFAB, position);
                        break;
                    case DropType.Minicopter:
                        CreateEntity<MiniCopter>(MINICOPTER_PREFAB, position);
                        break;
                    case DropType.Boat:
                        CreateEntity<MotorRowboat>(BOAT_PREFAB, position);
                        break;
                    case DropType.Rhib:
                        CreateEntity<MotorRowboat>(RHIB_PREFAB, position);
                        break;
                    case DropType.Car:
                        ModularCar modularCar = CreateEntity<ModularCar>(CAR_PREFAB, position);
                        
                        Interface.Oxide.NextTick(() =>
                        {
                            if (Tier < 1) return;
                            
                            if (modularCar.IsDead()) return;
                            
                            modularCar.SetHealth(modularCar.MaxHealth());

                            foreach (BaseVehicleModule moduleEntity in modularCar.AttachedModuleEntities)
                            {
                                moduleEntity.AdminFixUp(Tier);
                            }
                            
                            modularCar.SendNetworkUpdate();
                        });
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        #endregion

        #region Storage

        void LoadDefaultData() => _storage = new PluginData();

        void LoadData()
        {
            try
            {
                _storage = Interface.Oxide.DataFileSystem.ReadObject<PluginData>(Name);

                if (_storage == null)
                {
                    throw new JsonException();
                }
            }
            catch (Exception exception)
            {
                PrintWarning("Loaded default data.");

                LoadDefaultData();
            }
        }

        void ClearData()
        {
            _storage.Players.Clear();
            SaveData();
        }
        
        void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _storage);

        class PluginData
        {
            public Hash<ulong, PlayerData> Players = new Hash<ulong, PlayerData>();

            public PlayerData FindPlayerData(BasePlayer player)
            {
                PlayerData playerData;

                if (!Players.TryGetValue(player.userID, out playerData))
                {
                    Players[player.userID] = playerData = new PlayerData();
                }

                return playerData;
            }
        }

        class PlayerData
        {
            public Hash<string, DropData> DropData = new Hash<string, DropData>();
            
            public float GetCooldown(string name)
            {
               DropData dropData;

                if (!DropData.TryGetValue(name, out dropData))
                {
                    return 0f;
                }

                float currentTime = Time.time;

                return currentTime > dropData.Cooldown ? 0f : dropData.Cooldown - currentTime;
            }
            
            public bool HasReachedLimit(string name, int limit) => limit > 0 && GetUses(name) >= limit;
            
            public int GetUses(string name)
            {
               DropData dropData;

                return !DropData.TryGetValue(name, out dropData) ? 0 : dropData.Uses;
            }

            public void OnClaimed(DropConfig dropConfig)
            {
               DropData dropData;

                if (!DropData.TryGetValue(dropConfig.Name, out dropData))
                {
                    DropData[dropConfig.Name] = dropData = new DropData();
                }

                dropData.OnClaimed(dropConfig.Cooldown);
            }
        }

        class DropData
        {
            public int Uses;
            public float Cooldown;
            
            public void OnClaimed(float seconds)
            {
                Uses++;
                Cooldown = Time.time + seconds;
            }
        }
        
        #endregion
        
        #region Lang

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                { "Message.Prefix", "</color><color=#03cffc>VehicleDrops</color>: " },
                { "Message.Syntax", "<color=#e2e2e2>Invalid syntax, /vdrop <type>\n{0}.</color>" },
                { "Message.Limit", "<color=#e2e2e2>Sorry, limit reached.</color>" },
                { "Message.Dropped", "<color=#e2e2e2></color><color=#ffc55c>{0}</color> has been dropped at your location.</color>" },
                { "Message.Cooldown", "<color=#e2e2e2>Sorry, you are on cooldown <color=#ffc55c>{0}</color>.</color>" },
                { "Message.Give", "<color=#e2e2e2>You received a <color=#ffc55c>{0}</color>.</color>" },
                { "Error.Failed", "<color=#e2e2e2>Failed to give <color=#ffc55c>{0}</color>.</color>" },
                { "Error.NotFound", "<color=#e2e2e2>Failed to find drop.</color>" }
            }, this);
        }
        
        void MessagePlayer(BasePlayer player, string key, params object[] args)
            => MessagePlayer(player.IPlayer, key, args);

        void MessagePlayer(IPlayer player, string key, params object[] args)
            => player?.Reply(lang.GetMessage("Message.Prefix", this, player.Id ?? null) + string.Format(lang.GetMessage(key, this, player.Id ?? null), args));

        #endregion
        
        #region Oxide

        void OnServerInitialized()
        {
            AddCovalenceCommand("vdrop", nameof(GiveCommand), PERM_USE);
            
            LoadData();
            
            _config.RegisterDrops();
        }

        void Unload() => SaveData();

        void OnNewSave(string filename) => ClearData();

        void OnExplosiveThrown(BasePlayer player, SupplySignal supply, ThrownWeapon thrown) => 
            HandleSupplySignal(player, supply);

        void OnExplosiveDropped(BasePlayer player, SupplySignal supply, ThrownWeapon item) =>
            OnExplosiveThrown(player, supply, item);

        #endregion
        
        #region Vehicle Drop
        
        static T CreateEntity<T>(string prefab, Vector3 position) where T : BaseEntity
        {
            // Create entity based on given type.
            T entity = (T) GameManager.server.CreateEntity(prefab, position + Vector3.up * 100f);

            if (entity == null)
            {
                return null;
            }

            entity.Spawn();
            entity.GetOrAddComponent<CustomVehicleDrop>();
            entity.SetFlag(BaseEntity.Flags.On, true);
            entity.SendNetworkUpdateImmediate();
            
            EffectNetwork.Send(new Effect(FIRED_PREFAB, entity, 0, Vector3.zero, Vector3.zero));

            return entity;
        }
        
        void HandleSupplySignal(BasePlayer player, SupplySignal supply)
        {
            // Find drop config if skin id matches.
            DropConfig dropConfig = _config.FindDropBySkin(supply.skinID);
            
            if (dropConfig == null) return;
            
            // Cancel explode invoke to replace functionality with custom drop type.
            supply.CancelInvoke(supply.Explode);
            
            // Spawn custom drop at supply signal position.
            dropConfig.CreateEntity(supply.ServerPosition);
            
            // Re-invoke finish up after 10s.
            supply.Invoke(supply.FinishUp, 10f);
            supply.SetFlag(BaseEntity.Flags.On, true);
            supply.SendNetworkUpdateImmediate();

            MessagePlayer(player, "Message.Dropped", dropConfig.Name);
        }

        void CreateSupplySignal(BasePlayer player, string name)
        {
            DropConfig dropConfig = _config.FindDropByName(name);
            
            if (dropConfig == null)
            {
                MessagePlayer(player, "Error.NotFound");
                return;
            }
            
            PlayerData playerData = _storage.FindPlayerData(player);

            if (playerData.HasReachedLimit(dropConfig.Name, dropConfig.Limit))
            {
                MessagePlayer(player, "Message.Limit");
                return;
            }
            
            if (playerData.GetCooldown(dropConfig.Name) > 0)
            {
                MessagePlayer(player, "Message.Cooldown", FormatTime(playerData.GetCooldown(dropConfig.Name)));
                return;
            }

            if (!dropConfig.CreateSupply(player))
            {
                MessagePlayer(player, "Message.Failed");
                return;
            }

            playerData.OnClaimed(dropConfig);
            
            MessagePlayer(player, "Message.Give", dropConfig.Name);
        }

        string FormatTime(double time)
        {
            TimeSpan dateDifference = TimeSpan.FromSeconds(time);
            int days = dateDifference.Days;
            int hours = dateDifference.Hours;
            int mins = dateDifference.Minutes;
            int secs = dateDifference.Seconds;

            if (days > 0) return $"~{days:00}d:{hours:00}h";
            if (hours > 0) return $"~{hours:00}h:{mins:00}m";
            if (mins > 0) return $"{mins:00}m:{secs:00}s";

            return $"{secs}s";
        }
        
        class CustomVehicleDrop : MonoBehaviour
        {
            BaseEntity _vehicle;
            BaseEntity _parachute;
            Rigidbody _rigidbd;
            
            void Awake()
            {
                _vehicle = GetComponent<BaseEntity>();
                _rigidbd = GetComponent<Rigidbody>();
                
                CreateParachute();
            }
            
            void FixedUpdate()
            {
                // Keep moving vehicle down towards the world until vehicle has landed.
                if (_vehicle.IsValid() == true && !HasLanded())
                {
                    _vehicle.transform.position -= new Vector3(0, 10f * Time.deltaTime, 0);
                    
                    return;
                }

                Destroy(this);
            }

            void OnDestroy() => RemoveParachute();

            bool HasLanded()
            {
                return _vehicle.WaterFactor() >= 0.5f || 
                       Physics.OverlapSphereNonAlloc(transform.position, 0.5f, Vis.colBuffer, CollisionLayer) != 0;
            }
            
            void CreateParachute()
            {
                if (_rigidbd != null)
                    _rigidbd.useGravity = false;
                
                _parachute = GameManager.server.CreateEntity(PARACHUTE_PREFAB);
                _parachute.SetParent(_vehicle, "parachute_attach");
                _parachute.Spawn();
            }
            
            void RemoveParachute()
            {
                if (_vehicle.IsValid() == true && _rigidbd != null)
                    _rigidbd.useGravity = true;

                if (_parachute.IsValid() == true)
                {
                    _parachute.Kill();
                    _parachute = null;
                }
            }
        }

        #endregion

        #region Command
        
        void GiveCommand(IPlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                MessagePlayer(player, "Message.Syntax", string.Join("\n", _config.DropTypesByName.Keys));
                return;
            }

            CreateSupplySignal(player.ToBasePlayer(), string.Join(" ", args));
        }

        #endregion
    }

    #region VehicleDropEx

    namespace VehicleDropEx
    {
        public static class PlayerEx
        {
            public static BasePlayer ToBasePlayer(this IPlayer player) => player?.Object as BasePlayer;
        }
    }

    #endregion
}