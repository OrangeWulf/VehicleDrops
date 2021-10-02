using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using VLB;

namespace Oxide.Plugins
{
    [Info("VehicleDrops", "Bazz3l", "1.0.0")]
    [Description("")]
    public class VehicleDrops : CovalencePlugin
    {
        #region Fields
        
        private const string FIRED_PREFAB = "assets/prefabs/npc/m2bradley/effects/maincannonattack.prefab";
        private const string PARACHUTE_PREFAB = "assets/prefabs/misc/parachute/parachute.prefab";
        private const string SCRAP_HELICOPTER_PREFAB = "assets/content/vehicles/scrap heli carrier/scraptransporthelicopter.prefab";
        private const string MINICOPTER_PREFAB = "assets/content/vehicles/minicopter/minicopter.entity.prefab";
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
        
        private enum DropType
        {
            ScrapHelicopter = 0,
            Minicopter = 1,
            Boat = 2,
            Rhib = 4
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
            public Dictionary<ulong, DropConfig> DropConfigs = new Dictionary<ulong, DropConfig>();

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig
                {
                    DropConfigs = new Dictionary<ulong, DropConfig>
                    {
                        { 2144547783, new DropConfig("Scrap Helicopter", DropType.ScrapHelicopter) },
                        { 2144524645, new DropConfig("Minicopter", DropType.Minicopter) },
                        { 2144555007, new DropConfig("Row Boat", DropType.Boat) },
                        { 2144558893, new DropConfig("Rhib", DropType.Rhib) }
                    }
                };
            }
        }

        class DropConfig
        {
            public string DisplayName;
            public DropType DropType;

            public DropConfig(string displayName, DropType dropType)
            {
                DisplayName = displayName;
                DropType = dropType;
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
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        DropConfig FindDropConfig(ulong skinID)
        {
            DropConfig dropConfig;

            return _config.DropConfigs.TryGetValue(skinID, out dropConfig) ? dropConfig : null;
        }

        #endregion

        #region Lang

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                { "MessagePrefix", "</color><color=#03cffc>VehicleDrops</color>: " },
                { "MessageDropped", "<color=#e2e2e2></color><color=#03cffc>{0}</color> has been dropped at your location.</color>" }
            }, this);
        }

        string GetLang(string key, string id = null, params object[] args) =>
            lang.GetMessage("MessagePrefix", this, id) + string.Format(lang.GetMessage(key, this, id), args);

        #endregion
        
        #region Oxide

        void OnExplosiveThrown(BasePlayer player, SupplySignal supply, ThrownWeapon thrown) => 
            HandleSupplySignal(player, supply);

        void OnExplosiveDropped(BasePlayer player, SupplySignal supply, ThrownWeapon item) =>
            OnExplosiveThrown(player, supply, item);

        #endregion
        
        #region Vehicle Drop
        
        static void CreateEntity<T>(string prefab, Vector3 position) where T : BaseEntity
        {
            T entity = (T) GameManager.server.CreateEntity(prefab, position + Vector3.up * 100f);
            
            if (entity == null) return;
            
            entity.Spawn();
            entity.GetOrAddComponent<CustomVehicleDrop>();
            entity.SetFlag(BaseEntity.Flags.On, true);
            entity.SendNetworkUpdateImmediate();
            
            EffectNetwork.Send(new Effect(FIRED_PREFAB, entity, 0, Vector3.zero, Vector3.zero));
        }
        
        void HandleSupplySignal(BasePlayer player, SupplySignal supply)
        {
            // Find drop config if skin id matches.
            DropConfig dropConfig = FindDropConfig(supply.skinID);
            
            if (dropConfig == null) return;
            
            // Cancel explode invoke to replace functionality with custom drop type.
            supply.CancelInvoke(supply.Explode);
            
            // Spawn custom drop at supply signal position.
            dropConfig.CreateEntity(supply.ServerPosition);
            
            // Re-invoke finish up after 10s.
            supply.Invoke(supply.FinishUp, 10f);
            supply.SetFlag(BaseEntity.Flags.On, true);
            supply.SendNetworkUpdateImmediate();

            player.ChatMessage(GetLang("MessageDropped", player.UserIDString, dropConfig.DisplayName));
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
                       Physics.OverlapSphereNonAlloc(transform.position, 2.5f, Vis.colBuffer, CollisionLayer) != 0;
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

        #region API

        object CreateDrop(BasePlayer player, ulong skinID, int amount = 1)
        {
            DropConfig dropConfig = FindDropConfig(skinID);
            if (dropConfig == null) return null;

            Item item = ItemManager.CreateByName("supply.signal", amount, skinID);
            item.name = dropConfig.DisplayName;
            item.MarkDirty();
            
            player.GiveItem(item);
            
            return true;
        }

        #endregion
    }
}