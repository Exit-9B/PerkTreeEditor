// SPDX-License-Identifier: GPL-3.0-or-later
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;

namespace PerkTreeEditor;

internal class PerkTree
{
    public class Node
    {
        public required IFormLinkGetter<IPerkGetter> Perk { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public IReadOnlyList<uint> Children { get; set; } = [];
    };

    public Dictionary<uint, Node> Nodes { get; set; }

    public PerkTree(IActorValueInformationGetter actorValueInfo)
    {
        var perkTree = actorValueInfo.PerkTree;

        uint maxGridX = 0;
        foreach (var node in perkTree)
        {
            if (node.Index == 0)
                continue;

            maxGridX = Math.Max(maxGridX, node.PerkGridX ?? 0);
        }

        float offsetX = -maxGridX * 0.5f;

        Nodes = new(perkTree.Count);
        foreach (var node in perkTree)
        {
            var index = node.Index;
            if (!index.HasValue)
                continue;

            Nodes.Add(
                index.Value,
                new Node
                {
                    Perk = node.Perk,
                    X = (node.PerkGridX ?? 0) + (node.HorizontalPosition ?? 0f) + offsetX,
                    Y = (node.PerkGridY ?? 0) + (node.VerticalPosition ?? 0f),
                    Children = node.ConnectionLineToIndices,
                });
        }
    }
}
