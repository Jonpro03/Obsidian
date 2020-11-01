﻿using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.Blocks;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Items;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.Net.Packets.Play.Server;
using Obsidian.PlayerData;
using Obsidian.Serializer;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Extensions;
using Obsidian.Util.Registry;
using SharpCompress.Compressors.Deflate;
using System;
using System.Threading.Tasks;

namespace Obsidian
{
    public class PacketHandler
    {
        public static ILogger Logger => Globals.PacketLogger;

        public static async Task<Packet> ReadPacketAsync(MinecraftStream stream)
        {
            int length = await stream.ReadVarIntAsync();
            byte[] receivedData = new byte[length];

            await stream.ReadAsync(receivedData, 0, length);

            int packetId = 0;
            byte[] packetData = Array.Empty<byte>();

            using (var packetStream = new MinecraftStream(receivedData))
            {
                try
                {
                    packetId = await packetStream.ReadVarIntAsync();
                    int arlen = 0;

                    if (length - packetId.GetVarIntLength() > -1)
                        arlen = length - packetId.GetVarIntLength();

                    packetData = new byte[arlen];
                    await packetStream.ReadAsync(packetData, 0, packetData.Length);
                }
                catch
                {
                    throw;
                }
            }

            return new Packet(packetId, packetData);
        }

        public static async Task<Packet> ReadCompressedPacketAsync(MinecraftStream stream)
        {
            var packetLength = await stream.ReadVarIntAsync();
            var dataLength = await stream.ReadVarIntAsync();

            using var deStream = new MinecraftStream(new ZlibStream(stream, SharpCompress.Compressors.CompressionMode.Decompress, CompressionLevel.BestSpeed));

            var packetId = await deStream.ReadVarIntAsync();
            var packetData = await deStream.ReadUInt8ArrayAsync(dataLength - packetId.GetVarIntLength());

            return new Packet(packetId, packetData);
        }

        public static async Task HandlePlayPackets(Packet packet, Client client)
        {
            Server server = client.Server;
            Player player = client.Player;

            switch (packet.id)
            {
                case 0x00: // Teleport Confirm
                    var confirm = PacketSerializer.FastDeserialize<TeleportConfirm>(packet.data);

                    if (confirm.TeleportId == player.TeleportId)
                        break;

                    await player.KickAsync("Invalid teleport... cheater?");
                    //await player.TeleportAsync(player.LastLocation);//Teleport them back we didn't send this packet
                    break;

                case 0x01:
                    // Query Block NBT
                    Logger.LogDebug("Received query block nbt");
                    break;

                case 0x02://Set difficulty

                    break;

                case 0x03:
                    // Incoming chat message
                    var message = await PacketSerializer.FastDeserializeAsync<IncomingChatMessage>(packet.data);

                    await server.ParseMessageAsync(message.Message, client);
                    break;

                case 0x04:
                    // Client status
                    break;
                case 0x05:
                    // Client Settings
                    client.ClientSettings = PacketSerializer.FastDeserialize<ClientSettings>(packet.data);
                    Logger.LogDebug("Received client settings");
                    break;

                case 0x06:
                    // Tab-Complete
                    Logger.LogDebug("Received tab-complete");
                    break;

                case 0x07:
                    //TODO look more into this
                    // Window Confirmation (serverbound)
                    var conf = PacketSerializer.FastDeserialize<WindowConfirmation>(packet.data);

                    Logger.LogDebug("Window Confirmation (serverbound)");
                    break;

                case 0x08:
                    // Click Window Button
                    var clicked = PacketSerializer.FastDeserialize<ClickWindowButton>(packet.data);

                    break;

                case 0x09:// Click Window
                    {
                        var window = PacketSerializer.FastDeserialize<ClickWindow>(packet.data);

                        Logger.LogDebug("Click window");

                        if (window.WindowId == 0)
                        {

                            //This is the player inventory
                            switch (window.Mode)
                            {
                                case InventoryOperationMode.MouseClick://TODO InventoryClickEvent
                                    {
                                        if (window.Button == 0)
                                        {
                                            player.Inventory.RemoveItem(window.ClickedSlot, 64);
                                        }
                                        else
                                        {
                                            player.Inventory.RemoveItem(window.ClickedSlot, window.Item.Count / 2);
                                        }
                                        break;
                                    }

                                case InventoryOperationMode.ShiftMouseClick:
                                    break;
                                case InventoryOperationMode.NumberKeys:
                                    break;
                                case InventoryOperationMode.MiddleMouseClick:
                                    break;
                                case InventoryOperationMode.Drop:
                                    {
                                        //If clicked slot is -999 that means they clicked outside the inventory
                                        if (window.ClickedSlot != -999)
                                        {
                                            Logger.LogDebug("Dropped Item");
                                            if (window.Button == 0)
                                                player.Inventory.RemoveItem(window.ClickedSlot);
                                            else
                                                player.Inventory.RemoveItem(window.ClickedSlot, 64);
                                        }
                                        break;
                                    }
                                case InventoryOperationMode.MouseDrag:
                                    {
                                        if (window.ClickedSlot == -999)
                                        {
                                            if (window.Button == 0 || window.Button == 4 || window.Button == 8)
                                            {
                                                client.isDragging = true;
                                            }
                                            else if (window.Button == 2 || window.Button == 6 || window.Button == 10)
                                            {
                                                client.isDragging = false;
                                            }
                                        }
                                        else if (client.isDragging)
                                        {
                                            if (player.Gamemode == Gamemode.Creative)
                                            {
                                                if (window.Button != 9)
                                                    break;

                                                //creative copy
                                                player.Inventory.SetItem(window.ClickedSlot, new ItemStack(window.Item.Id, window.Item.Count)
                                                {
                                                    Nbt = window.Item.Nbt
                                                });
                                            }
                                            else
                                            {
                                                if (window.Button != 1 || window.Button != 5)
                                                    break;

                                                //survival painting
                                                player.Inventory.SetItem(window.ClickedSlot, new ItemStack(window.Item.Id, window.Item.Count)
                                                {
                                                    Nbt = window.Item.Nbt
                                                });
                                            }
                                        }
                                        else
                                        {
                                            //It shouldn't get here
                                        }

                                        break;
                                    }
                                case InventoryOperationMode.DoubleClick:
                                    break;
                                default:
                                    break;
                            }
                        }
                        else
                        {

                        }
                        break;
                    }

                case 0x0A:
                    // Close Window (serverbound)
                    var closedWindow = PacketSerializer.FastDeserialize<CloseWindow>(packet.data);

                    Logger.LogDebug("Received close window");
                    break;

                case 0x0B: // Plugin Message (serverbound)
                    {
                        var msg = await PacketSerializer.DeserializeAsync<PluginMessage>(packet.data);

                        var result = await msg.HandleAsync();

                        switch (result.Type)
                        {
                            case PluginMessageType.Brand:
                                client.Brand = result.Value.ToString();
                                break;
                            case PluginMessageType.Register:
                                {
                                    using var stream = new MinecraftStream(msg.PluginData);
                                    var len = await stream.ReadVarIntAsync();

                                    //Idk how this would work so I'm assuming a length will be sent first
                                    for (int i = 0; i < len; i++)
                                        server.RegisteredChannels.Add(await stream.ReadStringAsync());

                                    break;
                                }
                            case PluginMessageType.UnRegister:
                                server.RegisteredChannels.RemoveWhere(x => x == msg.Channel.ToLower());
                                break;
                            case PluginMessageType.Custom://This can be ignored for now
                            default:
                                break;
                        }

                        Logger.LogDebug($"Received plugin message: {msg.Channel}");
                    }
                    break;

                case 0x0C:
                    // Edit Book
                    Logger.LogDebug("Received edit book");
                    break;

                case 0x0E:
                    //Interact Entity
                    Logger.LogDebug("Interact entity");
                    break;

                case 0x0F:
                    //Generate structure
                    break;

                case 0x10:
                    // Keep Alive (serverbound)
                    var keepalive = PacketSerializer.FastDeserialize<KeepAlive>(packet.data);
                    Logger.LogDebug($"Successfully kept alive player {player.Username} with ka id " +
                        $"{keepalive.KeepAliveId} previously missed {client.missedKeepalives - 1} ka's"); // missed is 1 more bc we just handled one

                    // Server is alive, reset missed keepalives.
                    client.missedKeepalives = 0;
                    break;

                case 0x11://Lock Difficulty
                    break;

                case 0x12:
                    // Player Position
                    var pos = PacketSerializer.FastDeserialize<PlayerPosition>(packet.data);

                    await player.UpdateAsync(server, pos.Position, pos.OnGround);

                    break;

                case 0x13:
                    //Player Position And rotation (serverbound)
                    var ppos = PacketSerializer.FastDeserialize<ServerPlayerPositionLook>(packet.data);

                    await player.UpdateAsync(server, ppos.Position, ppos.Yaw, ppos.Pitch, ppos.OnGround);

                    break;

                case 0x14:// Player rotation
                    var look = PacketSerializer.FastDeserialize<PlayerRotation>(packet.data);

                    await player.UpdateAsync(server, look.Yaw, look.Pitch, look.OnGround);
                    break;

                case 0x15://Player movement
                    break;

                case 0x16:// Vehicle Move (serverbound)
                    break;

                case 0x17://Steer boat
                    break;

                case 0x18:
                    // Pick Item
                    var item = PacketSerializer.FastDeserialize<PickItem>(packet.data);

                    Logger.LogDebug("Received pick item");

                    break;

                case 0x19:
                    // Craft Recipe Request
                    Logger.LogDebug("Received craft recipe request");

                    break;

                case 0x1A:
                    // Player Abilities (serverbound)
                    Logger.LogDebug("Received player abilities");
                    break;

                case 0x1B:
                    // Player Digging

                    var digging = PacketSerializer.FastDeserialize<PlayerDigging>(packet.data);

                    await server.BroadcastPlayerDigAsync(new PlayerDiggingStore
                    {
                        Player = player.Uuid,
                        Packet = digging
                    });

                    break;

                case 0x1C:
                    //TODO Entity Action
                    var action = PacketSerializer.FastDeserialize<EntityAction>(packet.data);


                    switch (action.Action)
                    {
                        case EAction.StartSneaking:
                            player.Sneaking = true;
                            break;
                        case EAction.StopSneaking:
                            player.Sneaking = false;
                            break;
                        case EAction.LeaveBed:
                            player.Sleeping = false;
                            break;
                        case EAction.StartSprinting:
                            player.Sprinting = true;
                            break;
                        case EAction.StopSprinting:
                            player.Sprinting = false;
                            break;
                        case EAction.StartJumpWithHorse:
                            break;
                        case EAction.StopJumpWithHorse:
                            break;
                        case EAction.OpenHorseInventory:
                            player.InHorseInventory = true;
                            break;
                        case EAction.StartFlyingWithElytra:
                            player.FlyingWithElytra = true;
                            break;
                        default:
                            break;
                    }
                    Logger.LogDebug("Received entity action");
                    break;

                case 0x1D:
                    // Steer Vehicle
                    break;

                case 0x1E:// Set Displayed Recipe
                    break;

                case 0x1F://Set recipe book state
                    break;

                case 0x20:// Name Item
                    var nameItem = PacketSerializer.FastDeserialize<NameItem>(packet.data);
                    break;

                case 0x21://Resource pack status
                    break;

                case 0x22://Advancement tab
                    break;

                case 0x23://Select trade
                    break;

                case 0x24://Set beacon effect
                    break;

                case 0x25:// Held Item Change (serverbound)
                    var heldItemChange = PacketSerializer.FastDeserialize<ServerHeldItemChange>(packet.data);
                    player.CurrentSlot = (short)(heldItemChange.Slot + 36);

                    var heldItem = player.GetHeldItem();

                    await server.BroadcastPacketAsync(new EntityEquipment
                    {
                        EntityId = client.id,
                        Slot = ESlot.MainHand,
                        Item = new ItemStack
                        {
                            Present = heldItem.Present,
                            Count = (sbyte)heldItem.Count,
                            Id = heldItem.Id,
                            Nbt = heldItem.Nbt
                        }
                    }, player);

                    Logger.LogDebug("Held item change");
                    break;

                case 0x26://Update command block
                    break;

                case 0x27://Update command block minecart
                    break;

                case 0x28:// Creative Inventory Action in creative they send this to replace whatever item existed in the slot
                    {
                        var ca = PacketSerializer.FastDeserialize<CreativeInventoryAction>(packet.data);

                        player.Inventory.SetItem(ca.ClickedSlot, new ItemStack(ca.ClickedItem.Id, ca.ClickedItem.Count)
                        {
                            Nbt = ca.ClickedItem.Nbt,
                            Present = ca.ClickedItem.Present
                        });

                        if (ca.ClickedSlot >= 36 && ca.ClickedSlot <= 44)
                        {
                            heldItem = player.GetHeldItem();
                            await server.BroadcastPacketAsync(new EntityEquipment
                            {
                                EntityId = client.id,
                                Slot = ESlot.MainHand,
                                Item = new ItemStack
                                {
                                    Present = heldItem.Present,
                                    Count = (sbyte)heldItem.Count,
                                    Id = heldItem.Id,
                                    Nbt = heldItem.Nbt
                                }
                            }, player);
                        }
                    }
                    break;

                case 0x29://Update jigsaw block
                    break;

                case 0x2A://Update structure block
                    break;

                case 0x2B://Update sign
                    break;

                case 0x2C:// Animation (serverbound)
                    var serverAnim = PacketSerializer.FastDeserialize<Animation>(packet.data);

                    //TODO broadcast entity animation to nearby players
                    switch (serverAnim.Hand)
                    {
                        case Hand.MainHand:
                            await server.BroadcastPacketAsync(new EntityAnimation
                            {
                                EntityId = client.id,
                                Animation = EAnimation.SwingMainArm
                            }, player);
                            break;
                        case Hand.OffHand:
                            await server.BroadcastPacketAsync(new EntityAnimation
                            {
                                EntityId = client.id,
                                Animation = EAnimation.SwingOffhand
                            }, player);
                            break;
                        default:
                            break;
                    }
                    break;

                case 0x2D://Spectate
                    break;
                case 0x2E:// Player Block Placement
                    {
                        var pbp = PacketSerializer.FastDeserialize<PlayerBlockPlacement>(packet.data);

                        var currentItem = player.GetHeldItem();

                        var block = Registry.GetBlock(currentItem.Type);

                        var location = pbp.Location;

                        var interactedBlock = server.World.GetBlock(location);

                        if (interactedBlock.CanInteract() && !player.Sneaking)
                        {
                            var arg = await server.Events.InvokeBlockInteractAsync(new BlockInteractEventArgs(player, block, pbp.Location));

                            if (arg.Cancel)
                                return;

                            //TODO open chests/Crafting inventory ^ ^

                            Logger.LogDebug($"Block Interact: {interactedBlock} - {location}");

                            return;
                        }

                        if (player.Gamemode != Gamemode.Creative)
                            player.Inventory.RemoveItem(player.CurrentSlot);

                        switch (pbp.Face) // TODO fix this for logs
                        {
                            case BlockFace.Bottom:
                                location.Y -= 1;
                                break;

                            case BlockFace.Top:
                                location.Y += 1;
                                break;

                            case BlockFace.North:
                                location.Z -= 1;
                                break;

                            case BlockFace.South:
                                location.Z += 1;
                                break;

                            case BlockFace.West:
                                location.X -= 1;
                                break;

                            case BlockFace.East:
                                location.X += 1;
                                break;

                            default:
                                break;
                        }

                        block.Location = location;
                        server.World.SetBlock(location, block);

                        await server.BroadcastBlockPlacementAsync(player, block, location);
                        break;
                    }
                case 0x2F://Use item
                    Logger.LogDebug("Use item");
                    break;
            }
        }
    }
}