﻿using Obsidian.API;
using Obsidian.PlayerData;

namespace Obsidian.Net.Packets.Play.Client.GameState
{
    public class ChangeGamemodeState : ChangeGameState<Gamemode>
    {
        public override Gamemode Value { get; set; }

        public ChangeGamemodeState(Gamemode newMode) => this.Value = newMode;
    }

}
