using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AutoLoot
{
    public static class PatchMethod
    {
        private static System.Random rndGenerator = new System.Random((int)DateTime.Now.ToBinary());
        private static GameRandom gameRandom = new GameRandom();
        private static void GiveItemDelegate(EntityPlayer _ep, ItemValue _itemValue, int _itemCount)
        {
            if (_ep is EntityPlayerLocal)
            {
                ItemStack itemStack = new ItemStack(_itemValue, _itemCount);
                if (!LocalPlayerUI.GetUIForPlayer(_ep as EntityPlayerLocal).xui.PlayerInventory.AddItem(itemStack))
                {
                    GameManager.Instance.ItemDropServer(itemStack, _ep.GetPosition(), Vector3.zero, _ep.entityId, 60f, false);
                }
            }
            else
            {
                ClientInfo clientInfo = ConnectionManager.Instance.Clients.ForEntityId(_ep.entityId);

                if (clientInfo != null)
                {
                    EntityItem entityItem = (EntityItem)EntityFactory.CreateEntity(new EntityCreationData
                    {
                        entityClass = EntityClass.FromString("item"),
                        id = EntityFactory.nextEntityID++,
                        itemStack = new ItemStack(_itemValue, _itemCount),
                        pos = _ep.position,
                        rot = new Vector3(0f, 0f, 0f),
                        lifetime = 60f,
                        belongsPlayerId = _ep.entityId
                    });

                    GameManager.Instance.World.SpawnEntityInWorld(entityItem);
                    clientInfo.SendPackage(NetPackageManager.GetPackage<NetPackageEntityCollect>().Setup(entityItem.entityId, _ep.entityId));
                    GameManager.Instance.World.RemoveEntity(entityItem.entityId, EnumRemoveEntityReason.Killed);
                }
            }
        }

        private static void GiveItem(EntityPlayer _ep, ItemStack _item)
        {
            ThreadManager.AddSingleTaskMainThread("GiveItem", delegate
            {
                GiveItemDelegate(_ep, _item.itemValue, _item.count);
            }, null);
        }

        private static void GiveItem(EntityPlayer _ep, ItemValue _itemValue, int _itemCount)
        {
            ThreadManager.AddSingleTaskMainThread("GiveItem", delegate
            {
                GiveItemDelegate(_ep, _itemValue, _itemCount);
            }, null);
        }

        private static void GiveContainerItems(EntityAlive killed, EntityAlive killer)
        {
            float prob = EffectManager.GetValue(PassiveEffects.LootDropProb, killer.inventory.holdingItemItemValue, killed.lootDropProb, killer, null, default(FastTags), true, true, true, true, 1, true, false);
            killed.lootDropProb = float.MinValue;
            if((float)rndGenerator.NextDouble() <= prob)
            {
                EntityClass lootClass = EntityClass.GetEntityClass(EntityClass.FromString(killed.EntityClass.Properties.GetString("LootDropEntityClass")));
                string lootList = lootClass.Properties.GetString("LootListOnDeath");
                EntityPlayer entityPlayer = killer as EntityPlayer;
                float containerMod = 0f;
                float containerBonus = 0f;
                TileEntityLootContainer tileEntityLootContainer = new TileEntityLootContainer(null)
                {
                    entityId = EntityFactory.nextEntityID++,
                    lootListName = lootList
                };

                tileEntityLootContainer.SetContainerSize(LootContainer.GetLootContainer(lootList, true).size, true);
                LootContainer lootContainer = LootContainer.GetLootContainer(tileEntityLootContainer.lootListName, true);

                if (lootContainer != null)
                {
                    int percentage = (!lootContainer.useUnmodifiedLootstage) ? entityPlayer.GetHighestPartyLootStage(containerMod, containerBonus) : entityPlayer.unModifiedGameStage;
                    IList<ItemStack> items = lootContainer.Spawn(GameManager.Instance.World.GetGameRandom(), tileEntityLootContainer.items.Length, (float)percentage, 0f, entityPlayer, FastTags.none, true, false);

                    for(int i = 0; i < items.Count; i++)
                    {
                        GiveItem(entityPlayer, items[i]);
                    }
                }
            }   
        }


        private static void GiveZombieLoot(EntityAlive killed, EntityAlive killer)
        {

            EntityClass entityClass = killed.EntityClass;
            List<Block.SItemDropProb> harvestList;
            List<Block.SItemDropProb> DestoryList;

            if (!entityClass.itemsToDrop.TryGetValue(EnumDropEvent.Harvest, out harvestList))
            {
                harvestList = null;
            }

            if (!entityClass.itemsToDrop.TryGetValue(EnumDropEvent.Destroy, out DestoryList))
            {
                DestoryList = null;
            }

            if (harvestList != null)
            {
                for(int i = 0; i < harvestList.Count; i++)
                {
                    Block.SItemDropProb prob = harvestList[i];
                    float hCount = EffectManager.GetValue(PassiveEffects.HarvestCount, null, 1f, killer, null, FastTags.Parse(prob.tag), true, true, true, true, 1, true, false);
                    int itemCount = (int)((float)gameRandom.RandomRange(prob.minCount, prob.maxCount + 1) * hCount);
                    ItemValue itemValue = new ItemValue(ItemClass.GetItem(prob.name, false).type, false);
                    if (gameRandom.RandomFloat <= prob.prob && (itemCount - itemCount / 3) > 0)
                    {
                        GiveItem(killer as EntityPlayer, itemValue, itemCount);
                    }
                }
            }
            if (DestoryList != null)
            {
                for (int i = 0; i < DestoryList.Count; i++)
                {
                    Block.SItemDropProb prob = DestoryList[i];
                    float hCount = EffectManager.GetValue(PassiveEffects.HarvestCount, null, 1f, killer, null, FastTags.Parse(prob.tag), true, true, true, true, 1, true, false);
                    int itemCount = (int)((float)gameRandom.RandomRange(prob.minCount, prob.maxCount + 1) * hCount);
                    ItemValue itemValue = new ItemValue(ItemClass.GetItem(prob.name, false).type, false);
                    if (gameRandom.RandomFloat <= prob.prob && (itemCount - itemCount / 3) > 0)
                    {
                        GiveItem(killer as EntityPlayer, itemValue, itemCount);
                    }
                }
            }
            killed.timeStayAfterDeath = 0;

        }

        public static bool Prefix_OnEntityDeath(EntityAlive __instance, EntityAlive ___entityThatKilledMe)
        {
            EntityAlive killed = __instance;
            EntityAlive killer = ___entityThatKilledMe;

            if (killed.entityType == EntityType.Zombie || killed.EntityName.IndexOf("zombie", StringComparison.OrdinalIgnoreCase) >= 0 || killed.entityType == EntityType.Animal || killed.entityType == EntityType.Unknown)
            {
                if (killer == null)
                {
                    killer = (killed.GetAttackTarget() != null) ? killed.GetAttackTarget() : ((killed.aiClosestPlayer != null) ? killed.aiClosestPlayer : null);
                }

                if (killer != null && killer is EntityPlayer)
                {
                    GiveContainerItems(killed, killer);
                    GiveZombieLoot(killed, killer);
                }
            }

            return true;
        }
    }
    
    public static class PatchHelper
    {
        private static bool bPatched = false;
        private static Harmony harmonyInstance = new Harmony("AutoLoot");
        public static void DoPatches()
        {
            try
            {
                if (!bPatched)
                {
                    Patch();
                    bPatched = true;
                }
            }
            catch (Exception e)
            {
                Log.Out(string.Format("Error in Hook: {0} Stack:\r\n {1}", e.Message, e.StackTrace));
            }
        }
            
        
            
        private static void Patch()
        {
            MethodInfo methodOriginal = typeof(EntityAlive).GetMethod("OnEntityDeath");
            if (methodOriginal == null)
            {
                throw new Exception("Missing_Method: EntityAlive.OnEntityDeath");
            }
            else
            {
                MethodInfo methodPrefix = typeof(PatchMethod).GetMethod("Prefix_OnEntityDeath");
                if (methodPrefix == null)
                {
                    throw new Exception("Missing_Method: EntityAlive.OnEntityDeath");
                }

                harmonyInstance.Patch(methodOriginal, new HarmonyMethod(methodPrefix), null, null);
            }
        }
        
    }
    
}
