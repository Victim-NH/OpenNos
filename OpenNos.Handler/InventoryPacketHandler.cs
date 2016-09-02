﻿/*
 * This file is part of the OpenNos Emulator Project. See AUTHORS file for Copyright information
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 */

using OpenNos.Core;
using OpenNos.Domain;
using OpenNos.GameObject;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace OpenNos.Handler
{
    public class InventoryPacketHandler : IPacketHandler
    {
        #region Members

        private readonly ClientSession _session;

        #endregion

        #region Instantiation

        public InventoryPacketHandler(ClientSession session)
        {
            _session = session;
        }

        #endregion

        #region Properties

        public ClientSession Session { get { return _session; } }

        #endregion

        #region Methods

        [Packet("#req_exc")]
        public void AcceptExchange(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(' ', '^');
            byte mode;
            long charId;
            string charName;
            if (!byte.TryParse(packetsplit[2], out mode) || !long.TryParse(packetsplit[3], out charId)) return;

            if (Session.Character.MapId != ServerManager.Instance.GetProperty<short>(charId, "MapId"))
            {
                ServerManager.Instance.SetProperty(charId, "ExchangeInfo", null);
                Session.Character.ExchangeInfo = null;
            }
            else
            {
                if (mode == 2)
                {
                    bool otherInExchangeOrTrade = ServerManager.Instance.GetProperty<bool>(charId, "InExchangeOrTrade");

                    if (!Session.Character.InExchangeOrTrade || !otherInExchangeOrTrade)
                    {
                        if (charId == Session.Character.CharacterId) return;
                        if (Session.Character.Speed == 0)
                            return;

                        Session.Client.SendPacket($"exc_list 1 {charId} -1");
                        ExchangeInfo exc = new ExchangeInfo
                        {
                            CharId = charId,
                            Confirm = false
                        };
                        Session.Character.ExchangeInfo = exc;
                        ServerManager.Instance.SetProperty(charId, "ExchangeInfo", new ExchangeInfo { CharId = Session.Character.CharacterId, Confirm = false });
                        Session.CurrentMap?.Broadcast(Session, $"exc_list 1 {Session.Character.CharacterId} -1", ReceiverType.OnlySomeone, "", charId);
                    }
                    else
                        Session.CurrentMap?.Broadcast(Session, Session.Character.GenerateModal(Language.Instance.GetMessageFromKey("ALREADY_EXCHANGE"), 0), ReceiverType.OnlySomeone, "", charId);
                }
                else if (mode == 5)
                {
                    charName = ServerManager.Instance.GetProperty<string>(charId, "Name");

                    ServerManager.Instance.SetProperty(charId, "ExchangeInfo", null);
                    Session.Character.ExchangeInfo = null;

                    Session.Client.SendPacket(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("YOU_REFUSED"), 10));
                    Session.CurrentMap?.Broadcast(Session, Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("EXCHANGE_REFUSED"), Session.Character.Name), 10), ReceiverType.OnlySomeone, "", charId);
                }
            }
        }

        [Packet("#b_i")]
        public void AnswerToDelete(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(' ', '^');
            byte type; byte.TryParse(packetsplit[2], out type);
            short slot; short.TryParse(packetsplit[3], out slot);

            if (Convert.ToInt32(packetsplit[4]) == 1)
            {
                Session.Client.SendPacket(Session.Character.GenerateDialog($"#b_i^{type}^{slot}^2 #b_i^0^0^5 {Language.Instance.GetMessageFromKey("SURE_TO_DELETE")}"));
            }
            else if (Convert.ToInt32(packetsplit[4]) == 2)
            {
                if (Session.Character.InExchangeOrTrade)
                    return;
                Session.Character.DeleteItem(type, slot);
            }
        }

        [Packet("b_i")]
        public void AskToDelete(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(' ');
            byte type; byte.TryParse(packetsplit[2], out type);
            short slot; short.TryParse(packetsplit[3], out slot);
            Session.Client.SendPacket(Session.Character.GenerateDialog($"#b_i^{type}^{slot}^1 #b_i^0^0^5 {Language.Instance.GetMessageFromKey("ASK_TO_DELETE")}"));
        }

        [Packet("eqinfo")]
        public void EqInfo(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(' ');
            if (packetsplit.Length <= 3) return;

            byte type; byte.TryParse(packetsplit[2], out type);
            short slot; short.TryParse(packetsplit[3], out slot);
            WearableInstance inventory = null;
            switch (type)
            {
                case 0:
                    inventory = Session.Character.EquipmentList.LoadBySlotAndType<WearableInstance>(slot, (byte)InventoryType.Equipment);
                    if (inventory == null)
                        inventory = Session.Character.EquipmentList.LoadBySlotAndType<SpecialistInstance>(slot, (byte)InventoryType.Equipment);
                    break;

                case 1:
                    inventory = Session.Character.InventoryList.LoadBySlotAndType<WearableInstance>(slot, (byte)InventoryType.Wear);
                    if (inventory == null)
                        inventory = Session.Character.InventoryList.LoadBySlotAndType<SpecialistInstance>(slot, (byte)InventoryType.Wear);
                    break;

                case 2:
                    inventory = new WearableInstance(Guid.NewGuid());//TODO take GUID generation to GO
                    break;

                case 5:
                    if (Session.Character.ExchangeInfo != null)
                    {
                        byte inven;
                        if (byte.TryParse(packetsplit[3], out inven) && short.TryParse(packetsplit[4], out slot))
                        {
                            InventoryList inv = ServerManager.Instance.GetProperty<InventoryList>(Session.Character.ExchangeInfo.CharId, "InventoryList");
                            inventory = inv.LoadBySlotAndType<WearableInstance>(slot, inven);
                        }
                    }
                    break;

                case 7:
                    inventory = Session.Character.InventoryList.LoadBySlotAndType<SpecialistInstance>(slot, (byte)InventoryType.Sp); // Partner inv
                    break;

                case 10:
                    inventory = Session.Character.InventoryList.LoadBySlotAndType<SpecialistInstance>(slot, (byte)InventoryType.Sp);
                    break;

                case 11:
                    inventory = Session.Character.InventoryList.LoadBySlotAndType<WearableInstance>(slot, (byte)InventoryType.Costume);
                    break;
            }

            if (inventory != null && inventory.Item != null)
            {
                Session.Client.SendPacket(inventory.Item.EquipmentSlot != (byte)EquipmentType.Sp ? Session.Character.GenerateEInfo(inventory) : inventory.Item.SpType == 0 ? Session.Character.GeneratePslInfo(inventory as SpecialistInstance, 0) : Session.Character.GenerateSlInfo(inventory as SpecialistInstance, 0));
            }
        }

        [Packet("req_exc")]
        public void Exchange(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(' ');
            short mode;
            if (!short.TryParse(packetsplit[2], out mode)) return;

            long charId = -1;
            string charName;
            bool Blocked, otherBlocked;
            if (mode == 1)
            {
                if (!long.TryParse(packetsplit[3], out charId)) return;
                charName = (string)ServerManager.Instance.GetProperty<string>(charId, "Name");
                otherBlocked = ServerManager.Instance.GetProperty<bool>(charId, "ExchangeBlocked");
                Blocked = Session.Character.ExchangeBlocked;

                if (Session.Character.Speed == 0 || ServerManager.Instance.GetProperty<byte>(charId, "Speed") == 0)
                    Blocked = true;

                if (ServerManager.Instance.GetProperty<DateTime>(charId, "LastSkill").AddSeconds(20) > DateTime.Now || ServerManager.Instance.GetProperty<DateTime>(charId, "LastDefence").AddSeconds(20) > DateTime.Now)
                {
                    Session.Client.SendPacket(Session.Character.GenerateInfo(String.Format(Language.Instance.GetMessageFromKey("PLAYER_IN_BATTLE"), charName)));
                    return;
                }

                if (Session.Character.LastSkill.AddSeconds(20) > DateTime.Now || Session.Character.LastDefence.AddSeconds(20) > DateTime.Now)
                {
                    Session.Client.SendPacket(Session.Character.GenerateInfo(Language.Instance.GetMessageFromKey("IN_BATTLE")));
                    return;
                }

                if (otherBlocked || Blocked)
                {
                    if (Session.Character.HasShopOpened || ServerManager.Instance.GetProperty<bool>(charId, "HasShopOpened"))
                        Session.Client.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("HAS_SHOP_OPENED"), 10));
                    else
                        Session.Client.SendPacket(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("TRADE_BLOCKED"), 11));
                }
                else
                {
                    bool InExchangeOrTrade = Session.Character.InExchangeOrTrade, otherInExchangeOrTrade = ServerManager.Instance.GetProperty<bool>(charId, "InExchangeOrTrade");
                    if (InExchangeOrTrade || otherInExchangeOrTrade)
                        Session.Client.SendPacket(Session.Character.GenerateModal(Language.Instance.GetMessageFromKey("ALREADY_EXCHANGE"), 0));
                    else
                    {
                        if (Session.Character.Speed == 0)
                            return;

                        Session.Client.SendPacket(Session.Character.GenerateModal(String.Format(Language.Instance.GetMessageFromKey("YOU_ASK_FOR_EXCHANGE"), charName), 0));
                        Session.CurrentMap?.Broadcast(Session, Session.Character.GenerateDialog($"#req_exc^2^{Session.Character.CharacterId} #req_exc^5^{Session.Character.CharacterId} {String.Format(Language.Instance.GetMessageFromKey("INCOMING_EXCHANGE"), Session.Character.Name)}"), ReceiverType.OnlySomeone, charName);
                    }
                }
            }
            else if (mode == 3)
            {
                if (Session.Character.ExchangeInfo.CharId == Session.Character.CharacterId) return;
                ExchangeInfo exchange = ServerManager.Instance.GetProperty<ExchangeInfo>(Session.Character.ExchangeInfo.CharId, "ExchangeInfo");
                long gold = ServerManager.Instance.GetProperty<long>(Session.Character.ExchangeInfo.CharId, "Gold");
                int backpack = ServerManager.Instance.GetProperty<int>(Session.Character.ExchangeInfo.CharId, "BackPack");
                InventoryList inventory = ServerManager.Instance.GetProperty<InventoryList>(Session.Character.ExchangeInfo.CharId, "InventoryList");
                if (Session.Character.ExchangeInfo.Validate && exchange.Validate)
                {
                    Session.Character.ExchangeInfo.Confirm = true;
                    if (exchange.Confirm && Session.Character.ExchangeInfo.Confirm)
                    {
                        Session.Client.SendPacket("exc_close 1");
                        Session.CurrentMap?.Broadcast(Session, "exc_close 1", ReceiverType.OnlySomeone, "", Session.Character.ExchangeInfo.CharId);

                        bool continu = true;
                        bool goldmax = false;
                        bool notsold = false;
                        if (!Session.Character.InventoryList.GetFreePlaceAmount(exchange.ExchangeList, Session.Character.BackPack))
                            continu = false;

                        if (!inventory.GetFreePlaceAmount(Session.Character.ExchangeInfo.ExchangeList, backpack))
                            continu = false;

                        if (Session.Character.ExchangeInfo.Gold + gold > 1000000000)
                            goldmax = true;
                        if (Session.Character.ExchangeInfo.Gold > Session.Character.Gold)
                            return;
                        if (exchange.Gold + Session.Character.Gold > 1000000000)
                            goldmax = true;

                        if (continu == false)
                        {
                            string message = Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("NOT_ENOUGH_PLACE"), 0);
                            Session.Client.SendPacket(message);
                            Session.CurrentMap?.Broadcast(Session, message, ReceiverType.OnlySomeone, "", Session.Character.ExchangeInfo.CharId);

                            Session.Client.SendPacket("exc_close 0");
                            Session.CurrentMap?.Broadcast(Session, "exc_close 0", ReceiverType.OnlySomeone, "", Session.Character.ExchangeInfo.CharId);

                            ServerManager.Instance.SetProperty(Session.Character.ExchangeInfo.CharId, "ExchangeInfo", null);
                            Session.Character.ExchangeInfo = null;
                        }
                        else if (goldmax == true)
                        {
                            string message = Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("MAX_GOLD"), 0);
                            Session.Client.SendPacket(message);
                            Session.CurrentMap?.Broadcast(Session, message, ReceiverType.OnlySomeone, "", Session.Character.ExchangeInfo.CharId);

                            Session.Client.SendPacket("exc_close 0");
                            Session.CurrentMap?.Broadcast(Session, "exc_close 0", ReceiverType.OnlySomeone, "", Session.Character.ExchangeInfo.CharId);

                            ServerManager.Instance.SetProperty(Session.Character.ExchangeInfo.CharId, "ExchangeInfo", null);
                            Session.Character.ExchangeInfo = null;
                        }
                        else
                        {
                            foreach (ItemInstance item in Session.Character.ExchangeInfo.ExchangeList)
                            {
                                Inventory inv = Session.Character.InventoryList.GetInventoryByItemInstanceId(item.Id);
                                if (inv != null && (!ServerManager.GetItem(inv.ItemInstance.ItemVNum).IsTradable || inv.ItemInstance.IsUsed))
                                {
                                    Session.Client.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("ITEM_NOT_TRADABLE"), 0));
                                    Session.Client.SendPacket("exc_close 0");
                                    Session.CurrentMap?.Broadcast(Session, "exc_close 0", ReceiverType.OnlySomeone, "", Session.Character.ExchangeInfo.CharId);

                                    ServerManager.Instance.SetProperty(Session.Character.ExchangeInfo.CharId, "ExchangeInfo", null);
                                    Session.Character.ExchangeInfo = null;
                                    notsold = true;
                                    break;
                                }
                            }
                            if (!notsold)
                            {
                                foreach (ItemInstance item in Session.Character.ExchangeInfo.ExchangeList)
                                {
                                    // Delete items from their owners
                                    Inventory invtemp = Session.Character.InventoryList.Inventory.FirstOrDefault(s => s.ItemInstance.Id == item.Id);
                                    short slot = invtemp.Slot;
                                    byte type = invtemp.Type;
                                    Inventory inv = Session.Character.InventoryList.RemoveItemAmountFromInventory((byte)item.Amount, invtemp.Id);
                                    if (inv != null)
                                    {
                                        // Send reduced-amount to owners inventory
                                        Session.Client.SendPacket(Session.Character.GenerateInventoryAdd(inv.ItemInstance.ItemVNum, inv.ItemInstance.Amount, inv.Type, inv.Slot, inv.ItemInstance.Rare, inv.ItemInstance.Design, inv.ItemInstance.Upgrade, 0));
                                    }
                                    else
                                    {
                                        // Send empty slot to owners inventory
                                        Session.Client.SendPacket(Session.Character.GenerateInventoryAdd(-1, 0, type, slot, 0, 0, 0, 0));
                                    }
                                }

                                foreach (ItemInstance item in exchange.ExchangeList)
                                {
                                    // Add items to their new owners
                                    Inventory inv = Session.Character.InventoryList.AddToInventory(item);
                                    if (inv != null && inv.Slot != -1)
                                        Session.Client.SendPacket(
                                            Session.Character.GenerateInventoryAdd(inv.ItemInstance.ItemVNum,
                                                inv.ItemInstance.Amount, inv.Type, inv.Slot, inv.ItemInstance.Rare,
                                                 inv.ItemInstance.Design, inv.ItemInstance.Upgrade, 0));
                                }

                                Session.Character.Gold = Session.Character.Gold - Session.Character.ExchangeInfo.Gold + exchange.Gold;
                                Session.Client.SendPacket(Session.Character.GenerateGold());
                                ServerManager.Instance.ExchangeValidate(Session, Session.Character.ExchangeInfo.CharId);
                            }
                        }
                    }
                    else
                    {
                        charName = ServerManager.Instance.GetProperty<string>(charId, "Name");
                        Session.Client.SendPacket(Session.Character.GenerateInfo(String.Format(Language.Instance.GetMessageFromKey("IN_WAITING_FOR"), charName)));
                    }
                }
            }
            else if (mode == 4)
            {
                Session.Client.SendPacket("exc_close 0");
                Session.CurrentMap?.Broadcast(Session, "exc_close 0", ReceiverType.OnlySomeone, "", Session.Character.ExchangeInfo.CharId);

                ServerManager.Instance.SetProperty(Session.Character.ExchangeInfo.CharId, "ExchangeInfo", null);
                Session.Character.ExchangeInfo = null;
            }
        }

        [Packet("exc_list")]
        public void ExchangeList(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(' ');
            long Gold = 0; long.TryParse(packetsplit[2], out Gold);
            byte[] type = new byte[10];
            short[] slot = new short[10];
            byte[] qty = new byte[10];
            string packetList = "";
            if (Gold < 0 || Gold > Session.Character.Gold || Session.Character.ExchangeInfo.ExchangeList.Any())
                return;
            for (int j = 6, i = 0; j <= packetsplit.Length; j += 3, i++)
            {
                byte.TryParse(packetsplit[j - 3], out type[i]);
                short.TryParse(packetsplit[j - 2], out slot[i]);
                byte.TryParse(packetsplit[j - 1], out qty[i]);
                Inventory item = Session.Character.InventoryList.LoadInventoryBySlotAndType(slot[i], type[i]);
                if (qty[i] <= 0 || item.ItemInstance.Amount < qty[i])
                    return;
                ItemInstance it = (item.ItemInstance as ItemInstance).DeepCopy();

                if (it.Item.IsTradable)
                {
                    it.Amount = qty[i];
                    Session.Character.ExchangeInfo.ExchangeList.Add(it);
                    if (type[i] != 0)
                        packetList += $"{i}.{type[i]}.{it.ItemVNum}.{qty[i]} ";
                    else
                        packetList += $"{i}.{type[i]}.{it.ItemVNum}.0.0 ";
                }
                if (it.IsUsed)
                {
                    Session.Client.SendPacket("exc_close 0");
                    Session.CurrentMap?.Broadcast(Session, $"exc_close 0", ReceiverType.OnlySomeone, "", Session.Character.ExchangeInfo.CharId);

                    ServerManager.Instance.SetProperty(Session.Character.ExchangeInfo.CharId, "ExchangeInfo", null);
                    Session.Character.ExchangeInfo = null;
                    return;
                }
            }
            Session.Character.ExchangeInfo.Gold = Gold;
            Session.CurrentMap?.Broadcast(Session, $"exc_list 1 {Session.Character.CharacterId} {Gold} {packetList}", ReceiverType.OnlySomeone, "", Session.Character.ExchangeInfo.CharId);
            Session.Character.ExchangeInfo.Validate = true;
        }

        [Packet("get")]
        public void GetItem(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(' ');

            if (Session.Character.LastSkill.AddSeconds(1) > DateTime.Now)
            {
                return;
            }

            long transportId; long.TryParse(packetsplit[4], out transportId);
            MapItem mapitem;
            if (Session.CurrentMap.DroppedList.TryGetValue(transportId, out mapitem))
            {
                //rarify
                mapitem.Rarify(Session);

                int amount = mapitem.ItemInstance.Amount;

                if (mapitem.PositionX < Session.Character.MapX + 3 && mapitem.PositionX > Session.Character.MapX - 3 && mapitem.PositionY < Session.Character.MapY + 3 && mapitem.PositionY > Session.Character.MapY - 3)
                {
                    if (mapitem.ItemInstance.ItemVNum != 1046)
                    {
                        if (mapitem.ItemInstance.Item.ItemType == (byte)ItemType.Map)
                        {
                            Session.CurrentMap.DroppedList.Remove(transportId);
                            Session.CurrentMap?.Broadcast(Session.Character.GenerateGet(transportId));
                        }
                        else
                        {
                            Inventory newInv = Session.Character.InventoryList.AddToInventory(mapitem.ItemInstance);
                            if (newInv != null)
                            {
                                Session.CurrentMap.DroppedList.Remove(transportId);
                                Session.CurrentMap?.Broadcast(Session.Character.GenerateGet(transportId));
                                Session.Client.SendPacket(Session.Character.GenerateInventoryAdd(newInv.ItemInstance.ItemVNum, newInv.ItemInstance.Amount, newInv.Type, newInv.Slot, mapitem.ItemInstance.Rare, mapitem.ItemInstance.Design, mapitem.ItemInstance.Upgrade, 0));
                                Session.Client.SendPacket(Session.Character.GenerateSay($"{Language.Instance.GetMessageFromKey("ITEM_ACQUIRED")}: {(newInv.ItemInstance as ItemInstance).Item.Name} x {amount}", 12));
                            }
                            else
                            {
                                Session.Client.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("NOT_ENOUGH_PLACE"), 0));
                            }
                        }
                    }
                    else
                    {
                        Item iteminfo = ServerManager.GetItem(mapitem.ItemInstance.ItemVNum);
                        if (Session.Character.Gold + mapitem.ItemInstance.Amount <= 1000000000)
                        {
                            Session.Character.Gold += mapitem.ItemInstance.Amount;
                            Session.Client.SendPacket(Session.Character.GenerateSay($"{Language.Instance.GetMessageFromKey("ITEM_ACQUIRED")}: {iteminfo.Name} x {amount}", 12));
                        }
                        else
                        {
                            Session.Character.Gold = 1000000000;
                            Session.Client.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("MAX_GOLD"), 0));
                        }
                        Session.Client.SendPacket(Session.Character.GenerateGold());
                        Session.CurrentMap.DroppedList.Remove(transportId);
                        Session.CurrentMap?.Broadcast(Session.Character.GenerateGet(transportId));
                    }
                }
            }
        }

        [Packet("mve")]
        public void MoveInventory(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(' ');
            byte type; byte.TryParse(packetsplit[2], out type);
            short slot; short.TryParse(packetsplit[3], out slot);
            byte desttype; byte.TryParse(packetsplit[4], out desttype);
            short destslot; short.TryParse(packetsplit[5], out destslot);
            if (destslot > 48 + (Session.Character.BackPack * 12))
                return;
            if (Session.Character.InExchangeOrTrade)
                return;

            Inventory inv = Session.Character.InventoryList.MoveInventory(Session.Character.InventoryList.LoadInventoryBySlotAndType(slot, type), desttype, destslot);
            if (inv != null)
            {
                Session.Client.SendPacket(Session.Character.GenerateInventoryAdd(inv.ItemInstance.ItemVNum, inv.ItemInstance.Amount, desttype, inv.Slot, inv.ItemInstance.Rare, inv.ItemInstance.Design, inv.ItemInstance.Upgrade, 0));
                Session.Client.SendPacket(Session.Character.GenerateInventoryAdd(-1, 0, type, slot, 0, 0, 0, 0));
            }
        }

        [Packet("mvi")]
        public void MoveItem(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(' ');
            byte type; byte.TryParse(packetsplit[2], out type);
            short slot; short.TryParse(packetsplit[3], out slot);
            byte amount; byte.TryParse(packetsplit[4], out amount);
            short destslot; short.TryParse(packetsplit[5], out destslot);
            Inventory LastInventory;
            Inventory NewInventory;
            if (destslot > 48 + (Session.Character.BackPack * 12))
                return;
            if (Session.Character.InExchangeOrTrade)
                return;
            Session.Character.InventoryList.MoveItem(type, slot, amount, destslot, out LastInventory, out NewInventory);
            if (NewInventory == null) return;
            Session.Client.SendPacket(Session.Character.GenerateInventoryAdd(NewInventory.ItemInstance.ItemVNum, NewInventory.ItemInstance.Amount, type, NewInventory.Slot, NewInventory.ItemInstance.Rare, NewInventory.ItemInstance.Design, NewInventory.ItemInstance.Upgrade, 0));
            if (LastInventory != null)
                Session.Client.SendPacket(Session.Character.GenerateInventoryAdd(LastInventory.ItemInstance.ItemVNum, LastInventory.ItemInstance.Amount, type, LastInventory.Slot, LastInventory.ItemInstance.Rare, LastInventory.ItemInstance.Design, LastInventory.ItemInstance.Upgrade, 0));
            else
            {
                Session.Character.DeleteItem(type, slot);
            }
        }

        [Packet("put")]
        public void PutItem(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(' ');
            byte type; byte.TryParse(packetsplit[2], out type);
            short slot; short.TryParse(packetsplit[3], out slot);
            byte amount; byte.TryParse(packetsplit[4], out amount);
            Inventory invitem = Session.Character.InventoryList.LoadInventoryBySlotAndType(slot, type);
            if (invitem != null && (invitem.ItemInstance as ItemInstance).Item.IsDroppable && (invitem.ItemInstance as ItemInstance).Item.IsTradable && !Session.Character.InExchangeOrTrade)
            {
                if (amount > 0 && amount < 100)
                {
                    MapItem DroppedItem = Session.Character.InventoryList.PutItem(type, slot, amount, ref invitem);
                    if (DroppedItem == null)
                    {
                        Session.Client.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("ITEM_NOT_DROPPABLE_HERE"), 0)); ;
                        return;
                    }
                    Session.Client.SendPacket(Session.Character.GenerateInventoryAdd(invitem.ItemInstance.ItemVNum, invitem.ItemInstance.Amount, type, invitem.Slot, invitem.ItemInstance.Rare, invitem.ItemInstance.Design, invitem.ItemInstance.Upgrade, 0));

                    if (invitem.ItemInstance.Amount == 0)
                        Session.Character.DeleteItem(invitem.Type, invitem.Slot);
                    if (DroppedItem != null)
                        Session.CurrentMap?.Broadcast($"drop {DroppedItem.ItemInstance.ItemVNum} {DroppedItem.ItemInstance.Id} {DroppedItem.PositionX} {DroppedItem.PositionY} {DroppedItem.ItemInstance.Amount} 0 -1");
                }
                else
                {
                    Session.Client.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("BAD_DROP_AMOUNT"), 0));
                }
            }
            else
            {
                Session.Client.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("ITEM_NOT_DROPPABLE"), 0));
            }
        }

        [Packet("remove")]
        public void Remove(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            // Undress Equipment
            string[] packetsplit = packet.Split(' ');
            if (packetsplit.Length > 3 && Session.CurrentMap.UserShops.FirstOrDefault(mapshop => mapshop.Value.OwnerId.Equals(Session.Character.CharacterId)).Value == null && (Session.Character.ExchangeInfo == null || Session.Character.ExchangeInfo?.ExchangeList.Count() == 0))
            {
                short slot;
                if (!short.TryParse(packetsplit[2], out slot)) return; // Invalid Number

                ItemInstance inventory = (slot != (byte)EquipmentType.Sp) ? Session.Character.EquipmentList.LoadBySlotAndType<WearableInstance>(slot, (byte)InventoryType.Equipment) : Session.Character.EquipmentList.LoadBySlotAndType<SpecialistInstance>(slot, (byte)InventoryType.Equipment);
                if (inventory == null) return; // This eqslot is not equipped
                double currentRunningSeconds = (DateTime.Now - Process.GetCurrentProcess().StartTime.AddSeconds(-50)).TotalSeconds;

                double timeSpanSinceLastSpUsage = currentRunningSeconds - Session.Character.LastSp;

                if (slot == (byte)EquipmentType.Sp && Session.Character.UseSp)
                {
                    if (Session.Character.LastSkill.AddSeconds(2) > DateTime.Now)
                    {
                        return;
                    }
                    Session.Character.LastSp = (DateTime.Now - Process.GetCurrentProcess().StartTime.AddSeconds(-50)).TotalSeconds;
                    new Task(() => RemoveSP(inventory.ItemVNum)).Start();
                }
                else if (slot == (byte)EquipmentType.Sp && !Session.Character.UseSp && timeSpanSinceLastSpUsage <= Session.Character.SpCooldown)
                {
                    Session.Client.SendPacket(Session.Character.GenerateMsg(string.Format(Language.Instance.GetMessageFromKey("SP_INLOADING"), Session.Character.SpCooldown - (int)Math.Round(timeSpanSinceLastSpUsage, 0)), 0));
                    return;
                }
                // Put item back to inventory
                Inventory inv = Session.Character.InventoryList.AddToInventory(inventory);
                if (inv == null) return;

                if (inv.Slot != -1)
                    Session.Client.SendPacket(
                        Session.Character.GenerateInventoryAdd(inventory.ItemVNum,
                            inv.ItemInstance.Amount, inv.Type, inv.Slot, inventory.Rare,
                            inventory.Design, inventory.Upgrade, 0));

                Session.Character.EquipmentList.DeleteFromSlotAndType(slot, (byte)InventoryType.Equipment);

                Session.Client.SendPacket(Session.Character.GenerateStatChar());
                Session.CurrentMap?.Broadcast(Session.Character.GenerateEq());
                Session.Client.SendPacket(Session.Character.GenerateEquipment());
                Session.CurrentMap?.Broadcast(Session.Character.GeneratePairy());
            }
        }

        [Packet("#sl")]
        public void Sl(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(' ', '^');
            byte mode;
            if (!byte.TryParse(packetsplit[2], out mode)) return;
            if (!Session.Character.UseSp)
            {
                double currentRunningSeconds = (DateTime.Now - Process.GetCurrentProcess().StartTime.AddSeconds(-50)).TotalSeconds;
                double timeSpanSinceLastSpUsage = currentRunningSeconds - Session.Character.LastSp;
                if (timeSpanSinceLastSpUsage >= Session.Character.SpCooldown)
                {
                    ChangeSP();
                }
            }
        }

        [Packet("sortopen")]
        public void SortOpen(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            Boolean gravity = true;
            byte type;
            while (gravity)
            {
                gravity = false;
                for (short x = 0; x < 44; x++)
                {
                    for (short i = 0; i < 2; i++)
                    {
                        type = (i == 0) ? (byte)InventoryType.Sp : (byte)InventoryType.Costume;
                        if (Session.Character.InventoryList.LoadBySlotAndType<ItemInstance>(x, type) == null)
                        {
                            if (Session.Character.InventoryList.LoadBySlotAndType<ItemInstance>((short)(x + 1), type) != null)
                            {
                                Inventory invdest = new Inventory();
                                Inventory inv = new Inventory();
                                Session.Character.InventoryList.MoveItem(type, (short)(x + 1), 1, x, out inv, out invdest);
                                WearableInstance wearableInstance = invdest.ItemInstance as WearableInstance;
                                Session.Client.SendPacket(Session.Character.GenerateInventoryAdd(invdest.ItemInstance.ItemVNum, invdest.ItemInstance.Amount, type, invdest.Slot, wearableInstance.Rare, wearableInstance.Design, wearableInstance.Upgrade, 0));
                                Session.Character.DeleteItem(type, (short)(x + 1));
                                gravity = true;
                            }
                        }
                    }
                }
            }
        }

        [Packet("#u_i")]
        public void SpecialUseItem(string packet)
        {
            UseItem(packet);
        }

        [Packet("sl")]
        public void SpTransform(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(' ');

            SpecialistInstance specialistInstance = Session.Character.EquipmentList.LoadBySlotAndType<SpecialistInstance>((byte)EquipmentType.Sp, (byte)InventoryType.Equipment);

            if (packetsplit.Length == 10 && packetsplit[2] == "10")
            {
                if (!Session.Character.UseSp || specialistInstance == null || int.Parse(packetsplit[5]) != specialistInstance.TransportId)
                {
                    Session.Client.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("SPUSE_NEEDED"), 0));
                    return;
                }
                if (ServersData.SpPoint(specialistInstance.SpLevel, specialistInstance.Upgrade)
                    - specialistInstance.SlDamage - specialistInstance.SlHP
                    - specialistInstance.SlElement - specialistInstance.SlDefence
                    - short.Parse(packetsplit[6]) - short.Parse(packetsplit[7])
                    - short.Parse(packetsplit[8]) - short.Parse(packetsplit[9]) < 0)
                    return;
                if (short.Parse(packetsplit[6]) < 0 || short.Parse(packetsplit[7]) < 0 || short.Parse(packetsplit[8]) < 0 || short.Parse(packetsplit[9]) < 0) { return; }
                specialistInstance.SlDamage += short.Parse(packetsplit[6]);
                specialistInstance.SlDefence += short.Parse(packetsplit[7]);
                specialistInstance.SlElement += short.Parse(packetsplit[8]);
                specialistInstance.SlHP += short.Parse(packetsplit[9]);

                int slElement = ServersData.SlPoint(specialistInstance.SlElement, 2);
                int slHp = ServersData.SlPoint(specialistInstance.SlHP, 3);
                int slDefence = ServersData.SlPoint(specialistInstance.SlDefence, 1);
                int slHit = ServersData.SlPoint(specialistInstance.SlDamage, 0);

                //so add upgrade to sp
                //slhit
                specialistInstance.DamageMinimum = 0;
                specialistInstance.DamageMaximum = 0;
                specialistInstance.HitRate = 0;
                specialistInstance.CriticalLuckRate = 0;
                specialistInstance.CriticalRate = 0;
                specialistInstance.DefenceDodge = 0;
                specialistInstance.DistanceDefenceDodge = 0;
                specialistInstance.ElementRate = 0;
                specialistInstance.DarkResistance = 0;
                specialistInstance.LightResistance = 0;
                specialistInstance.FireResistance = 0;
                specialistInstance.WaterResistance = 0;
                specialistInstance.CriticalDodge = 0;
                specialistInstance.MagicDefence = 0;
                specialistInstance.HP = 0;
                specialistInstance.MP = 0;

                if (slHit >= 1)
                {
                    specialistInstance.DamageMinimum += 5;
                    specialistInstance.DamageMaximum += 5;
                }
                if (slHit >= 10)
                {
                    specialistInstance.HitRate += 10;
                }
                if (slHit >= 20)
                {
                    specialistInstance.CriticalLuckRate += 2;
                }
                if (slHit >= 30)
                {
                    specialistInstance.DamageMinimum += 5;
                    specialistInstance.DamageMaximum += 5;
                    specialistInstance.HitRate += 10;
                }
                if (slHit >= 40)
                {
                    specialistInstance.CriticalRate += 10;
                }
                if (slHit >= 50)
                {
                    specialistInstance.HP += 200;
                    specialistInstance.MP += 200;
                }
                if (slHit >= 60)
                {
                    specialistInstance.HitRate += 15;
                }
                if (slHit >= 70)
                {
                    specialistInstance.HitRate += 15;
                    specialistInstance.DamageMinimum += 5;
                    specialistInstance.DamageMaximum += 5;
                }
                if (slHit >= 80)
                {
                    specialistInstance.CriticalLuckRate += 2;
                }
                if (slHit >= 90)
                {
                    specialistInstance.CriticalRate += 20;
                }
                //sldef
                if (slDefence >= 20)
                {
                    specialistInstance.DefenceDodge += 2;
                    specialistInstance.DistanceDefenceDodge += 2;
                }
                if (slDefence >= 30)
                {
                    specialistInstance.HP += 100;
                }
                if (slDefence >= 40)
                {
                    specialistInstance.DefenceDodge += 2;
                    specialistInstance.DistanceDefenceDodge += 2;
                }
                if (slDefence >= 60)
                {
                    specialistInstance.HP += 200;
                }
                if (slDefence >= 70)
                {
                    specialistInstance.DefenceDodge += 3;
                    specialistInstance.DistanceDefenceDodge += 3;
                }
                if (slDefence >= 75)
                {
                    specialistInstance.FireResistance += 2;
                    specialistInstance.WaterResistance += 2;
                    specialistInstance.LightResistance += 2;
                    specialistInstance.DarkResistance += 2;
                }
                if (slDefence >= 80)
                {
                    specialistInstance.DefenceDodge += 3;
                    specialistInstance.DistanceDefenceDodge += 3;
                }
                if (slDefence >= 90)
                {
                    specialistInstance.FireResistance += 3;
                    specialistInstance.WaterResistance += 3;
                    specialistInstance.LightResistance += 3;
                    specialistInstance.DarkResistance += 3;
                }
                if (slDefence >= 95)
                {
                    specialistInstance.HP += 300;
                }
                //slele
                if (slElement >= 1)
                {
                    specialistInstance.ElementRate += 2;
                }
                if (slElement >= 10)
                {
                    specialistInstance.MP += 100;
                }
                if (slElement >= 20)
                {
                    specialistInstance.MagicDefence += 5;
                }
                if (slElement >= 30)
                {
                    specialistInstance.FireResistance += 2;
                    specialistInstance.WaterResistance += 2;
                    specialistInstance.LightResistance += 2;
                    specialistInstance.DarkResistance += 2;
                    specialistInstance.ElementRate += 2;
                }
                if (slElement >= 40)
                {
                    specialistInstance.MP += 100;
                }
                if (slElement >= 50)
                {
                    specialistInstance.MagicDefence += 5;
                }
                if (slElement >= 60)
                {
                    specialistInstance.FireResistance += 3;
                    specialistInstance.WaterResistance += 3;
                    specialistInstance.LightResistance += 3;
                    specialistInstance.DarkResistance += 3;
                    specialistInstance.ElementRate += 2;
                }
                if (slElement >= 70)
                {
                    specialistInstance.MP += 100;
                }
                if (slElement >= 80)
                {
                    specialistInstance.MagicDefence += 5;
                }
                if (slElement >= 90)
                {
                    specialistInstance.FireResistance += 4;
                    specialistInstance.WaterResistance += 4;
                    specialistInstance.LightResistance += 4;
                    specialistInstance.DarkResistance += 4;
                }
                if (slElement == 100)
                {
                    specialistInstance.FireResistance += 6;
                    specialistInstance.WaterResistance += 6;
                    specialistInstance.LightResistance += 6;
                    specialistInstance.DarkResistance += 6;
                }
                //slhp
                if (slElement >= 5)
                {
                    specialistInstance.DamageMinimum += 5;
                    specialistInstance.DamageMaximum += 5;
                }
                if (slElement >= 10)
                {
                    specialistInstance.DamageMinimum += 5;
                    specialistInstance.DamageMaximum += 5;
                }
                if (slElement >= 15)
                {
                    specialistInstance.DamageMinimum += 5;
                    specialistInstance.DamageMaximum += 5;
                }
                if (slElement >= 20)
                {
                    specialistInstance.DamageMinimum += 5;
                    specialistInstance.DamageMaximum += 5;
                    specialistInstance.CloseDefence += 10;
                    specialistInstance.DistanceDefence += 10;
                    specialistInstance.MagicDefence += 10;
                }
                if (slElement >= 25)
                {
                    specialistInstance.DamageMinimum += 5;
                    specialistInstance.DamageMaximum += 5;
                }
                if (slElement >= 30)
                {
                    specialistInstance.DamageMinimum += 5;
                    specialistInstance.DamageMaximum += 5;
                }
                if (slElement >= 35)
                {
                    specialistInstance.DamageMinimum += 5;
                    specialistInstance.DamageMaximum += 5;
                }
                if (slElement >= 40)
                {
                    specialistInstance.DamageMinimum += 5;
                    specialistInstance.DamageMaximum += 5;
                    specialistInstance.CloseDefence += 15;
                    specialistInstance.DistanceDefence += 15;
                    specialistInstance.MagicDefence += 15;
                }
                if (slElement >= 45)
                {
                    specialistInstance.DamageMinimum += 10;
                    specialistInstance.DamageMaximum += 10;
                }
                if (slElement >= 50)
                {
                    specialistInstance.DamageMinimum += 10;
                    specialistInstance.DamageMaximum += 10;
                    specialistInstance.FireResistance += 2;
                    specialistInstance.WaterResistance += 2;
                    specialistInstance.LightResistance += 2;
                    specialistInstance.DarkResistance += 2;
                }
                if (slElement >= 60)
                {
                    specialistInstance.DamageMinimum += 10;
                    specialistInstance.DamageMaximum += 10;
                }
                if (slElement >= 65)
                {
                    specialistInstance.DamageMinimum += 10;
                    specialistInstance.DamageMaximum += 10;
                }
                if (slElement >= 70)
                {
                    specialistInstance.DamageMinimum += 10;
                    specialistInstance.DamageMaximum += 10;
                    specialistInstance.CloseDefence += 45;
                    specialistInstance.DistanceDefence += 45;
                    specialistInstance.MagicDefence += 45;
                }
                if (slElement >= 75)
                {
                    specialistInstance.DamageMinimum += 15;
                    specialistInstance.DamageMaximum += 15;
                }
                if (slElement >= 80)
                {
                    specialistInstance.DamageMinimum += 15;
                    specialistInstance.DamageMaximum += 15;
                }
                if (slElement >= 85)
                {
                    specialistInstance.DamageMinimum += 15;
                    specialistInstance.DamageMaximum += 15;
                    specialistInstance.CriticalDodge += 1;
                }
                if (slElement >= 86)
                {
                    specialistInstance.CriticalDodge += 1;
                }
                if (slElement >= 87)
                {
                    specialistInstance.CriticalDodge += 1;
                }
                if (slElement >= 88)
                {
                    specialistInstance.CriticalDodge += 1;
                }
                if (slElement >= 90)
                {
                    specialistInstance.DamageMinimum += 15;
                    specialistInstance.DamageMaximum += 15;
                    specialistInstance.DefenceDodge += (short)((slElement - 90) * 2);
                    specialistInstance.DistanceDefenceDodge += (short)((slElement - 90) * 2);
                }
                if (slElement >= 95)
                {
                    specialistInstance.DamageMinimum += 15;
                    specialistInstance.DamageMaximum += 15;
                }
                if (slElement >= 100)
                {
                    specialistInstance.DamageMinimum += 20;
                    specialistInstance.DamageMaximum += 20;
                    specialistInstance.FireResistance += 3;
                    specialistInstance.WaterResistance += 3;
                    specialistInstance.LightResistance += 3;
                    specialistInstance.DarkResistance += 3;
                    specialistInstance.CloseDefence += 30;
                    specialistInstance.DistanceDefence += 30;
                    specialistInstance.MagicDefence += 30;
                    specialistInstance.CriticalDodge += 3;
                }
                Session.Client.SendPacket(Session.Character.GenerateStatChar());
                Session.Client.SendPacket(Session.Character.GenerateStat());
                Session.Client.SendPacket(Session.Character.GenerateSlInfo(specialistInstance, 2));
                Session.Client.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("POINTS_SET"), 0));
            }
            else if (!Session.Character.IsSitting)
            {
                if (Session.Character.Skills.Where(s => s.Used == true).Any())
                {
                    Session.Client.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("SKILLS_IN_LOADING"), 0));
                    return;
                }

                if ((Session.Character.LastMove.AddSeconds(1) >= DateTime.Now || Session.Character.LastSkill.AddSeconds(2) >= DateTime.Now))
                {
                    return;
                }

                if (specialistInstance == null)
                {
                    Session.Client.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("NO_SP"), 0));
                    return;
                }

                double currentRunningSeconds = (DateTime.Now - Process.GetCurrentProcess().StartTime.AddSeconds(-50)).TotalSeconds;

                if (Session.Character.UseSp)
                {
                    Session.Character.LastSp = currentRunningSeconds;
                    new Task(() => RemoveSP(specialistInstance.ItemVNum)).Start();
                }
                else
                {
                    double timeSpanSinceLastSpUsage = currentRunningSeconds - Session.Character.LastSp;
                    if (timeSpanSinceLastSpUsage >= Session.Character.SpCooldown && !Session.Character.IsVehicled)
                    {
                        Session.Client.SendPacket("delay 5000 3 #sl^1");
                        Session.CurrentMap?.Broadcast($"guri 2 1 {Session.Character.CharacterId}");
                    }
                    else if (Session.Character.IsVehicled)
                    {
                        return;
                    }
                    else
                    {
                        Session.Client.SendPacket(Session.Character.GenerateMsg(string.Format(Language.Instance.GetMessageFromKey("SP_INLOADING"), Session.Character.SpCooldown - (int)Math.Round(timeSpanSinceLastSpUsage, 0)), 0));
                    }
                }
            }
        }

        [Packet("up_gr")]
        public void UpgradeCommand(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(' ');
            if ((Session.Character.ExchangeInfo != null && Session.Character.ExchangeInfo?.ExchangeList.Count() != 0) || Session.Character.Speed == 0)
                return;
            if (packetsplit.Count() > 4)
            {
                byte uptype, type, slot, type2 = 0, slot2 = 0;
                byte.TryParse(packetsplit[2], out uptype);
                byte.TryParse(packetsplit[3], out type);
                byte.TryParse(packetsplit[4], out slot);

                if (packetsplit.Count() > 6)
                {
                    byte.TryParse(packetsplit[5], out type2);
                    byte.TryParse(packetsplit[6], out slot2);
                }
                WearableInstance inventory;
                switch (uptype)
                {
                    case 1:
                        inventory = Session.Character.InventoryList.LoadBySlotAndType<WearableInstance>(slot, type);
                        if (inventory != null)
                        {
                            Item iteminfo = ServerManager.GetItem(inventory.ItemVNum);
                            if (iteminfo.EquipmentSlot == (byte)EquipmentType.Armor || iteminfo.EquipmentSlot == (byte)EquipmentType.MainWeapon || iteminfo.EquipmentSlot == (byte)EquipmentType.SecondaryWeapon)
                            {
                                inventory.UpgradeItem(Session, UpgradeMode.Normal, UpgradeProtection.None);
                            }
                        }
                        break;

                    case 7:
                        inventory = Session.Character.InventoryList.LoadBySlotAndType<WearableInstance>(slot, type);
                        if (inventory != null)
                        {
                            Item iteminfo = ServerManager.GetItem(inventory.ItemVNum);
                            if (iteminfo.EquipmentSlot == (byte)EquipmentType.Armor || iteminfo.EquipmentSlot == (byte)EquipmentType.MainWeapon || iteminfo.EquipmentSlot == (byte)EquipmentType.SecondaryWeapon)
                            {
                                inventory.RarifyItem(Session, RarifyMode.Normal, RarifyProtection.None);
                            }
                            Session.Client.SendPacket("shop_end 1");
                        }
                        break;

                    case 8:
                        inventory = Session.Character.InventoryList.LoadBySlotAndType<WearableInstance>(slot, type);
                        WearableInstance inventory2 = Session.Character.InventoryList.LoadBySlotAndType<WearableInstance>(slot2, type2);

                        if (inventory != null && inventory2 != null && inventory != inventory2)
                        {
                            inventory.SumItem(Session, inventory2);
                        }
                        break;

                    case 9:
                        SpecialistInstance specialist = Session.Character.InventoryList.LoadBySlotAndType<SpecialistInstance>(slot, type);
                        if (specialist != null)
                        {
                            Item iteminfo = ServerManager.GetItem(specialist.ItemVNum);
                            if (specialist.Rare != -2)
                            {
                                if (iteminfo.EquipmentSlot == (byte)EquipmentType.Sp)
                                {
                                    specialist.UpgradeSp(Session, UpgradeProtection.None);
                                    Session.Client.SendPacket(Session.Character.GenerateInventoryAdd(specialist.ItemVNum, 1, type, slot, specialist.Rare, specialist.Design, specialist.Upgrade, 0));
                                }
                            }
                            else
                                Session.Client.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("CANT_UPGRADE_DESTROYED_SP"), 0));
                        }
                        break;

                    case 41:
                        specialist = Session.Character.InventoryList.LoadBySlotAndType<SpecialistInstance>(slot, type);
                        if (specialist != null)
                        {
                            Item iteminfo = ServerManager.GetItem(specialist.ItemVNum);
                            if (specialist.Rare != -2)
                            {
                                if (iteminfo.EquipmentSlot == (byte)EquipmentType.Sp)
                                {
                                    specialist.PerfectSP(Session, UpgradeProtection.None);
                                    Session.Client.SendPacket(Session.Character.GenerateInventoryAdd(specialist.ItemVNum, 1, type, slot, specialist.Rare, specialist.Design, specialist.Upgrade, 0));
                                }
                            }
                            else Session.Client.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("CANT_UPGRADE_DESTROYED_SP"), 0));
                        }
                        break;
                }
            }
        }

        [Packet("u_i")]
        public void UseItem(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(' ', '^');
            if (packetsplit.Length > 5)
            {
                byte type; byte.TryParse(packetsplit[4], out type);
                short slot; short.TryParse(packetsplit[5], out slot);
                Inventory inv = Session.Character.InventoryList.LoadInventoryBySlotAndType(slot, type);
                if (inv != null)
                {
                    (inv.ItemInstance as ItemInstance).Item.Use(Session, ref inv, packetsplit[1].ElementAt(0) == '#');
                }
            }
        }

        [Packet("wear")]
        public void Wear(string packet)
        {
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(' ');
            if ((Session.Character.ExchangeInfo != null && Session.Character.ExchangeInfo?.ExchangeList.Count() != 0) || Session.Character.Speed == 0)
                return;
            if (packetsplit.Length > 3 && Session.CurrentMap.UserShops.FirstOrDefault(mapshop => mapshop.Value.OwnerId.Equals(Session.Character.CharacterId)).Value == null)
            {
                byte type;
                short slot;

                if (!byte.TryParse(packetsplit[3], out type) || !short.TryParse(packetsplit[2], out slot)) return;
                Inventory inv = Session.Character.InventoryList.LoadInventoryBySlotAndType(slot, type);
                if (inv != null && inv.ItemInstance != null && (inv.ItemInstance as ItemInstance).Item != null)
                {
                    (inv.ItemInstance as ItemInstance).Item.Use(Session, ref inv);
                }
            }
        }

        private void ChangeSP()
        {
            Logger.Debug("ChangeSP", Session.SessionId);
            Session.Character.LastTransform = DateTime.Now;
            SpecialistInstance sp = Session.Character.EquipmentList.LoadBySlotAndType<SpecialistInstance>((byte)EquipmentType.Sp, (byte)InventoryType.Equipment);
            WearableInstance fairy = Session.Character.EquipmentList.LoadBySlotAndType<WearableInstance>((byte)EquipmentType.Fairy, (byte)InventoryType.Equipment);
            if (sp == null)
                return;
            if (Session.Character.GetReputIco() < ServerManager.GetItem(sp.ItemVNum).ReputationMinimum)
            {
                Session.Client.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("LOW_REP"), 0));
                return;
            }
            if (fairy != null && ServerManager.GetItem(fairy.ItemVNum).Element != ServerManager.GetItem(sp.ItemVNum).Element && ServerManager.GetItem(fairy.ItemVNum).Element != ServerManager.GetItem(sp.ItemVNum).SecondaryElement)
            {
                Session.Client.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("BAD_FAIRY"), 0));
                return;
            }

            Session.Character.UseSp = true;
            Session.Character.Morph = ServerManager.GetItem(sp.ItemVNum).Morph;
            Session.Character.MorphUpgrade = sp.Upgrade;
            Session.Character.MorphUpgrade2 = sp.Design;
            Session.CurrentMap?.Broadcast(Session.Character.GenerateCMode());

            
            //qslot 0 1.1.2 1.1.1 1.1.3 0.7.-1 1.1.0 0.7.-1 0.7.-1 0.1.10 1.3.2 1.3.1
            //qslot 1 1.1.2 1.1.3 1.1.4 1.1.5 1.1.6 7.7.-1 7.7.-1 7.7.-1 7.7.-1 7.7.-1
            //qslot 2 7.7.-1 7.7.-1 7.7.-1 7.7.-1 7.7.-1 7.7.-1 7.7.-1 7.7.-1 7.7.-1 7.7.-1
            

            Session.CurrentMap?.Broadcast(Session.Character.GenerateEff(196));
            Session.CurrentMap?.Broadcast($"guri 6 1 {Session.Character.CharacterId} 0 0");
            Session.Client.SendPacket(Session.Character.GenerateSpPoint());
            Session.Character.Speed += ServerManager.GetItem(sp.ItemVNum).Speed;
            Session.Client.SendPacket(Session.Character.GenerateLev());
            Session.Client.SendPacket(Session.Character.GenerateStat());
            Session.Client.SendPacket(Session.Character.GenerateStatChar());
            Session.Character.SkillsSp = new List<CharacterSkill>();
            foreach (Skill ski in ServerManager.GetAllSkill())
            {
                if (ski.Class == Session.Character.Morph + 31 && sp.SpLevel >= ski.LevelMinimum)
                    Session.Character.SkillsSp.Add(new CharacterSkill() { SkillVNum = ski.SkillVNum, CharacterId = Session.Character.CharacterId });
            }

            Session.Client.SendPacket(Session.Character.GenerateSki());
            string[] quicklistpackets = Session.Character.GenerateQuicklist();
            foreach (string quicklist in quicklistpackets)
                Session.Client.SendPacket(quicklist);
        }

        private async void RemoveSP(short vnum)
        {
            if (Session.Character.IsVehicled)
                return;
            Logger.Debug(vnum.ToString(), Session.SessionId);
            SpecialistInstance sp = Session.Character.EquipmentList.LoadBySlotAndType<SpecialistInstance>((byte)EquipmentType.Sp, (byte)InventoryType.Equipment);
            Session.Character.Speed -= ServerManager.GetItem(vnum).Speed;
            Session.Character.UseSp = false;

            Session.Client.SendPacket(Session.Character.GenerateCond());
            Session.Client.SendPacket(Session.Character.GenerateLev());

            Session.Character.SpCooldown = 30;
            if (Session.Character != null && Session.Character.SkillsSp != null)
            {
                foreach (CharacterSkill ski in Session.Character.SkillsSp.Where(s => s.Used))
                {
                    short time = ServerManager.GetSkill(ski.SkillVNum).Cooldown;
                    double temp = (ski.LastUse - DateTime.Now).TotalMilliseconds + time * 100;
                    temp /= 1000;
                    Session.Character.SpCooldown = temp > Session.Character.SpCooldown ? (int)(temp) : (int)(Session.Character.SpCooldown);
                }
            }

            Session.Client.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("STAY_TIME"), Session.Character.SpCooldown), 11));

            Session.Client.SendPacket($"sd {Session.Character.SpCooldown}");

            Session.CurrentMap?.Broadcast(Session.Character.GenerateCMode());
            Session.CurrentMap?.Broadcast($"guri 6 1 {Session.Character.CharacterId} 0 0");

            //s="ms_c";
            //chara.Send(s);

            Session.Client.SendPacket(Session.Character.GenerateSki());
            string[] quicklistpackets = Session.Character.GenerateQuicklist();
            foreach (string quicklist in quicklistpackets)
                Session.Client.SendPacket(quicklist);
            Session.Client.SendPacket(Session.Character.GenerateStat());
            Session.Client.SendPacket(Session.Character.GenerateStatChar());

            await Task.Delay(Session.Character.SpCooldown * 1000);
            if (Session == null || Session.Client == null)
                return;
            Session.Client.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("TRANSFORM_DISAPPEAR")), 11));
            Session.Client.SendPacket("sd 0");
        }

        #endregion
    }
}