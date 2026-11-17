using System.Collections.Generic;
using DV.Customization;
using DV.Customization.Gadgets;
using DV.Utils;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Describes mounted tablets and speakers for other players and rebuilds them from a description. A copy is a real item, mounted like a loaded save. No Multiplayer types.</summary>
    public static class GadgetMounts
    {
        // Keys of the item save data, see GadgetItem.OnItemSaveDataRequestedInternal.
        private const string PlacedOnKey = "placedOn";
        private const string GadgetDataKey = "gadgetData";
        private const string ModKey = "dvrat";

        /// <summary>True while a remote mount or removal is applied, so it is not echoed back.</summary>
        public static bool ApplyingRemote { get; private set; }

        // GadgetItem.Item has a private setter. The item sets it in Start, a frame after creation.
        private static readonly System.Reflection.PropertyInfo ItemProperty = AccessTools.Property(typeof(GadgetItem), nameof(GadgetItem.Item));

        /// <summary>Null for gadgets that are not a tablet or speaker.</summary>
        public static string IdOf(GadgetBase gadget)
        {
            if (gadget == null) return null;
            var tablet = gadget.GetComponentInChildren<RATTabletController>(true);
            if (tablet != null) return tablet.Session.Id;
            var speaker = gadget.GetComponentInChildren<RATSpeaker>(true);
            return speaker != null ? speaker.Settings.Id : null;
        }

        /// <summary>Returns the item prefab and its save data.</summary>
        public static bool TryDescribe(GadgetBase gadget, out string prefab, out string state, out TrainCar car)
        {
            prefab = state = null;
            car = gadget != null ? gadget.TrainCar : null;
            var item = gadget != null ? gadget.GadgetItem : null;
            var save = item != null ? item.GetComponent<ItemSaveData>() : null;
            if (car == null || save == null || item.Item == null || item.Item.InventorySpecs == null) return false;
            prefab = item.Item.InventorySpecs.ItemPrefabName;
            state = save.SaveItemData().ToString(Newtonsoft.Json.Formatting.None);
            return true;
        }

        /// <summary>For a player who joins.</summary>
        public static List<GadgetBase> AllMounted()
        {
            var found = new List<GadgetBase>();
            foreach (var gadget in Object.FindObjectsOfType<GadgetBase>())
                if (gadget.IsLinked && gadget.TrainCar != null && IdOf(gadget) != null)
                    found.Add(gadget);
            return found;
        }

        public static GadgetBase Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var gadget in Object.FindObjectsOfType<GadgetBase>())
                if (gadget.IsLinked && IdOf(gadget) == id)
                    return gadget;
            return null;
        }

        /// <summary>Skips an ID that is already mounted, in case the Multiplayer mod syncs gadgets itself one day.</summary>
        public static void Mount(string id, string prefab, string state, TrainCar car)
        {
            if (Find(id) != null)
            {
                RATMain.Log($"Multiplayer: {prefab} {id} is mounted already, not adding it again");
                return;
            }
            var data = JObject.Parse(state);
            // Use this game's GUID for the car.
            data[PlacedOnKey] = car.CarGUID;
            var source = Resources.Load(prefab) as GameObject;
            if (source == null)
            {
                RATMain.LogWarning($"Multiplayer: no item {prefab} for a gadget another player mounted");
                return;
            }

            ApplyingRemote = true;
            GameObject copy = null;
            try
            {
                copy = Object.Instantiate(source, car.transform.position, Quaternion.identity);
                copy.name = prefab;
                copy.GetComponent<InventoryItemSpec>().BelongsToPlayer = true;
                // Place needs the item, which Start sets later. A loading save waits, a copy mounts at once.
                var gadgetItem = copy.GetComponent<GadgetItem>();
                if (gadgetItem != null && gadgetItem.Item == null)
                    ItemProperty.SetValue(gadgetItem, copy.GetComponent<DV.CabControls.ItemBase>());
                var save = copy.GetComponent<ItemSaveData>();
                // The game's path for a saved item: mounts where the data says, then loads the state.
                save.LoadItemData(data);
                save.PostLoadItemData();
                var gadget = gadgetItem?.Gadget;
                if (gadget == null || !gadget.IsLinked)
                {
                    RATMain.LogWarning($"Multiplayer: {prefab} {id} could not be mounted on {car.ID}");
                    Discard(copy);
                    return;
                }
                RATMain.Log($"Multiplayer: mounted {prefab} {id} on {car.ID}");
            }
            catch
            {
                // Unmount cannot remove a half-mounted copy without its item.
                Discard(copy);
                throw;
            }
            finally
            {
                ApplyingRemote = false;
            }
        }

        // For a copy that failed to mount.
        private static void Discard(GameObject copy)
        {
            if (copy == null) return;
            var gadget = copy.GetComponent<GadgetItem>()?.Gadget;
            if (gadget != null)
            {
                if (gadget.IsLinked) gadget.Unlink();
                Object.Destroy(gadget.gameObject);
            }
            SingletonBehaviour<StorageController>.Instance.RemoveItemFromStorageItemList(copy);
            Object.Destroy(copy);
        }

        /// <summary>The item is destroyed with it.</summary>
        public static void Unmount(string id)
        {
            var gadget = Find(id);
            if (gadget == null) return;
            ApplyingRemote = true;
            try
            {
                var item = gadget.Remove(false);
                if (item == null)
                {
                    // The game refused, e.g. another gadget sits on it. Destroying it anyway would break the save.
                    RATMain.LogWarning($"Multiplayer: another player took {id} off, but here it cannot be removed");
                    return;
                }
                SingletonBehaviour<StorageController>.Instance.RemoveItemFromWorldStorage(item.Item);
                Object.Destroy(item.gameObject);
                // The gadget is a separate object made by the item.
                Object.Destroy(gadget.gameObject);
                RATMain.Log($"Multiplayer: removed {id}, another player took it off");
            }
            finally
            {
                ApplyingRemote = false;
            }
        }
    }
}
