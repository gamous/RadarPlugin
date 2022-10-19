﻿using Dalamud.Hooking;
using Dalamud.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using RadarPlugin;

namespace RadarPlugin;

public class RadarLogic : IDisposable
{
    private const float PI = 3.14159265359f;
    private DalamudPluginInterface pluginInterface { get; set; }
    private Configuration configInterface { get; set; }
    private Task backgroundLoop { get; set; }
    private bool keepRunning { get; set; }
    private ObjectTable objectTable { get; set; }
    private List<GameObject> areaObjects { get; set; }
    private bool refreshing { get; set; }

    public RadarLogic(DalamudPluginInterface pluginInterface, Configuration configuration, ObjectTable objectTable)
    {
        // Creates Dependencies
        this.objectTable = objectTable;
        this.pluginInterface = pluginInterface;
        this.configInterface = configuration;

        // Loads plugin
        PluginLog.Debug($"Radar Loaded");
        keepRunning = true;
        // TODO: In the future adjust this
        this.pluginInterface.UiBuilder.Draw += DrawRadar;
        backgroundLoop = Task.Run(BackgroundLoop);

        areaObjects = new List<GameObject>();
    }

    private void DrawRadar()
    {
        if (!configInterface.Enabled) return;
        if (refreshing) return;
        foreach (var areaObject in areaObjects)
        {
            Vector2 onScreenPosition;
            var p = Services.GameGui.WorldToScreen(areaObject.Position, out onScreenPosition);
            if (!p) continue;

            var tagText = GetText(areaObject);

            if (areaObject is BattleChara mob)
            {
                if (mob.SubKind == 5) // Mobs
                {
                    DrawMob(onScreenPosition, mob, tagText);
                } else if (mob.SubKind == 4) // Players
                {
                    DrawPlayer(onScreenPosition, mob, tagText);
                }
            }
            else if (areaObject is GameObject obj)
            {
                if (UtilInfo.RenameList.ContainsKey(areaObject.DataId))
                {
                    tagText = $"{UtilInfo.RenameList[areaObject.DataId]}";
                }
                var tagTextSize = ImGui.CalcTextSize(tagText);
                
                ImGui.GetForegroundDrawList().AddText(
                    new Vector2(onScreenPosition.X - tagTextSize.X / 2f, onScreenPosition.Y + tagTextSize.Y / 2f),
                    UtilInfo.Color(0xFF, 0x7E, 0x00, 0xFF), //  #FF7E00
                    tagText);
            }
        }
    }

    private void DrawPlayer(Vector2 position, BattleChara chara, string mobText)
    {
        var tagTextSize = ImGui.CalcTextSize(chara.Name.TextValue);
        ImGui.GetForegroundDrawList().AddText(
            new Vector2(position.X - tagTextSize.X / 2f, position.Y + tagTextSize.Y / 2f),
            UtilInfo.Color(0x00, 0x99, 0x99, 0xff), //  #009999
            chara.Name.TextValue);
    }
    
    private void DrawMob(Vector2 position, BattleChara npc, string mobText)
    {
        if (true) // TODO: Make config option
        {
            DrawHealthCircle(position, npc.MaxHp, npc.CurrentHp);
        }

        var tagTextSize = ImGui.CalcTextSize(mobText);
        var colorWhite = UtilInfo.Color(0xff, 0xff, 0xff, 0xff);
        ImGui.GetForegroundDrawList().AddText(
            new Vector2(position.X - tagTextSize.X / 2f, position.Y + tagTextSize.Y / 2f),
            colorWhite,
            mobText);
    }

    private void DrawHealthCircle(Vector2 position, uint maxHp, uint currHp, bool includeText = true)
    {
        var radius = 13f;
        var v1 = (float)currHp / (float)maxHp;
        var aMax = PI * 2.0f;
        var difference = v1 - 1.0f;

        var healthText = ((int)(v1 * 100)).ToString();   
        var colorWhite = UtilInfo.Color(0xff, 0xff, 0xff, 0xff);
        var colorHealth = ImGui.ColorConvertFloat4ToU32(new Vector4(Math.Abs(v1 - difference), v1, v1, 1.0f));
        ImGui.GetForegroundDrawList().PathArcTo(position, radius,
            (-(aMax / 4.0f)) + (aMax / maxHp) * (maxHp - currHp), aMax - (aMax / 4.0f), 200 - 1);
        ImGui.GetForegroundDrawList().PathStroke(colorHealth, ImDrawFlags.None, 2.0f);
        if (includeText)
        {
            var healthTextSize = ImGui.CalcTextSize(healthText);
            ImGui.GetForegroundDrawList().AddText(
                new Vector2((position.X - healthTextSize.X / 2.0f), (position.Y - healthTextSize.Y / 2.0f)),
                colorWhite,
                healthText);
        }
    }

    private string GetText(GameObject obj)
    {
        if (configInterface.DebugMode)
        {
            return $"{obj.Name}, {obj.DataId}";
        }
        return $"{obj.Name}";
    }
    
    private void BackgroundLoop()
    {
        while (keepRunning)
        {
            if (configInterface.Enabled)
            {
                UpdateMobInfo();
                PluginLog.Debug("Refreshed Mob Info!");
            }

            Thread.Sleep(1000);
        }
    }

    private void UpdateMobInfo()
    {
        var nearbyMobs = new List<GameObject>();

        foreach (var obj in objectTable)
        {
            if (configInterface.DebugMode)
            {
                nearbyMobs.Add(obj);
                continue;
            }

            if (!obj.IsValid()) continue;
            if (obj is BattleChara mob)
            {
                if (obj is BattleNpc npc)
                {
                    if (npc.BattleNpcKind != BattleNpcSubKind.Enemy)
                        continue;
                }

                if (mob.CurrentHp <= 0) continue;
                if (!configInterface.ShowPlayers && obj.SubKind == 4) continue;
                if (UtilInfo.DataIdIgnoreList.Contains(mob.DataId) ||
                    configInterface.DataIdIgnoreList.Contains(mob.DataId)) continue;
                nearbyMobs.Add(obj);
            }
            else if (configInterface.ObjectShow)
            {
                if (UtilInfo.RenameList.ContainsKey(obj.DataId) ||
                    UtilInfo.ObjectStringList.Contains(obj.Name.TextValue))
                {
                    nearbyMobs.Add(obj);
                }
            }
        }

        refreshing = true; // TODO change off refreshing
        areaObjects.Clear();
        areaObjects.AddRange(nearbyMobs);
        refreshing = false;
    }

    public void Dispose()
    {
        keepRunning = false;
        while (!backgroundLoop.IsCompleted) ;
        PluginLog.Debug($"Radar Unloaded");
    }
}