// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using NiflySharp;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace PerkTreeEditor;

public partial class MainWindow : Window
{
    interface ISkillInfo
    {
        // TODO: Replace with a more flexible interface
        IActorValueInformationGetter ActorValueInfo { get; }
    }

    class ActorValueSkill : ISkillInfo
    {
        public ActorValueSkill(IActorValueInformationGetter actorValueInfo)
        {
            ActorValueInfo = actorValueInfo;
        }

        public IActorValueInformationGetter ActorValueInfo { get; }
        public string Label => ActorValueInfo.Name?.String ?? "";

        public override string ToString()
        {
            return ActorValueInfo.Name?.String ?? "";
        }
    }

    class SkillGroup
    {
        public required string Label;
        public required List<FormLink<IActorValueInformationGetter>> Skills;
        public required string SkydomePath;
        public string? MeshArchive;
        public required int CameraRightPoint;

        public override string ToString()
        {
            return Label;
        }

        public IEnumerable<ISkillInfo> GetSkillList(ILinkCache<ISkyrimMod, ISkyrimModGetter> cache)
        {
            foreach (var skill in Skills)
            {
                if (skill.TryResolve(cache, out var actorValueInfo))
                {
                    yield return new ActorValueSkill(actorValueInfo);
                }
            }
        }
    }

    private static readonly SkillGroup StandardSkills = new()
    {
        Label = "Standard Skills",
        Skills = [
            Skyrim.ActorValueInformation.AVEnchanting,
            Skyrim.ActorValueInformation.AVSmithing,
            Skyrim.ActorValueInformation.AVHeavyArmor,
            Skyrim.ActorValueInformation.AVBlock,
            Skyrim.ActorValueInformation.AVTwoHanded,
            Skyrim.ActorValueInformation.AVOneHanded,
            Skyrim.ActorValueInformation.AVMarksman,
            Skyrim.ActorValueInformation.AVLightArmor,
            Skyrim.ActorValueInformation.AVSneak,
            Skyrim.ActorValueInformation.AVLockpicking,
            Skyrim.ActorValueInformation.AVPickpocket,
            Skyrim.ActorValueInformation.AVSpeechcraft,
            Skyrim.ActorValueInformation.AVAlchemy,
            Skyrim.ActorValueInformation.AVMysticism,
            Skyrim.ActorValueInformation.AVConjuration,
            Skyrim.ActorValueInformation.AVDestruction,
            Skyrim.ActorValueInformation.AVRestoration,
        ],
        SkydomePath = "Meshes/Interface/INTPerkSkydome.nif",
        MeshArchive = "Skyrim - Meshes1.bsa",
        CameraRightPoint = 1,
    };

    private static readonly SkillGroup BeastSkills = new()
    {
        Label = "Beast Skills",
        Skills = [
            Skyrim.ActorValueInformation.AVHealRatePowerMod,
            Skyrim.ActorValueInformation.AVMagickaRateMod,
        ],
        SkydomePath = "Meshes/DLC01/Interface/INTVampirePerkSkydome.nif",
        MeshArchive = "Skyrim - Meshes0.bsa",
        CameraRightPoint = 2,
    };

    private readonly IGameEnvironment<ISkyrimMod, ISkyrimModGetter> GameEnv;
    private readonly Dictionary<string, WeakReference<Texture>> TextureHolder = [];
    private SkillGroup? SelectedGroup;

    public MainWindow()
    {
        InitializeComponent();

        SkydomeView view = this.FindControl<SkydomeView>("View")!;

        GameEnv = GameEnvironment.Typical.Skyrim(SkyrimRelease.SkyrimSE);
        var dataFolder = GameEnv.DataFolderPath;

        Texture? ResolveTexture(string path)
        {
            path = path.ToLower().Replace('/', '\\');
            if (TextureHolder.TryGetValue(path, out var textureRef))
            {
                if (textureRef.TryGetTarget(out var texture))
                {
                    return texture;
                }
            }

            using Stream? stream = FindTexture(dataFolder, path);
            if (stream is not null)
            {
                try
                {
                    var texture = new Texture(stream);
                    TextureHolder[path] = new(texture);
                    return texture;
                }
                catch { }
            }

            return default;
        }

        view.ResolveTexture = ResolveTexture;

        LoadCommonAssets(dataFolder);

        ComboBox groupCombo = this.FindControl<ComboBox>("SkillGroupCombo")!;
        ImmutableArray<SkillGroup> items = [StandardSkills, BeastSkills];
        groupCombo.ItemsSource = items;
        groupCombo.SelectedIndex = 0;
    }

    private void LoadCommonAssets(DirectoryPath dataFolder)
    {
        using (Stream? stream = FindAsset(
            dataFolder,
            "Meshes/Interface/INTPerkStars01.nif",
            "Skyrim - Meshes1.bsa"))
        {
            if (stream is not null)
            {
                var nif = new NifFile(stream);
                if (nif.Valid)
                {
                    this.FindControl<SkydomeView>("View")!.SetPerkStars(nif);
                }
            }
        }

        using (Stream? stream = FindAsset(
            dataFolder,
            "Meshes/Interface/INTPerkLine01.nif",
            "Skyrim - Meshes1.bsa"))
        {
            if (stream is not null)
            {
                var nif = new NifFile(stream);
                if (nif.Valid)
                {
                    this.FindControl<SkydomeView>("View")!.SetPerkLine(nif);
                }
            }
        }
    }

    private static Stream? FindAsset(DirectoryPath dataFolder, string path, string? archive)
    {
        var looseFile = new FilePath(Path.Join(dataFolder, path));
        if (looseFile.Exists)
        {
            return looseFile.OpenRead();
        }

        if (archive is null)
            return default;

        var reader = Archive.CreateReader(GameRelease.SkyrimSE, Path.Join(dataFolder, archive));
        if (reader.TryGetFolder(Path.GetDirectoryName(path)!, out var archiveFolder))
        {
            var file = archiveFolder.Files
                .Where(file => file.Path.Equals(path.ToLower().Replace('/', '\\')))
                .FirstOrDefault();

            return file?.AsStream();
        }

        return default;
    }

    private static Stream? FindTexture(DirectoryPath dataFolder, string path)
    {
        var looseFile = new FilePath(Path.Join(dataFolder, path));
        if (looseFile.Exists)
        {
            return looseFile.OpenRead();
        }

        for (int i = 0; i <= 8; ++i)
        {
            var archive = $"Skyrim - Textures{i}.bsa";
            var reader = Archive.CreateReader(GameRelease.SkyrimSE, Path.Join(dataFolder, archive));
            if (reader.TryGetFolder(Path.GetDirectoryName(path)!, out var archiveFolder))
            {
                var file = archiveFolder.Files
                    .Where(file => file.Path.Equals(path.ToLower().Replace('/', '\\')))
                    .FirstOrDefault();

                if (file is not null)
                {
                    return file.AsStream();
                }
            }
        }

        return default;
    }

    private void Window_Closed(object? sender, System.EventArgs e)
    {
        GameEnv.Dispose();
    }

    private void SkillGroupCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count != 1)
            return;

        var skillGroup = e.AddedItems[0] as SkillGroup;
        if (skillGroup is null)
            return;

        SelectedGroup = skillGroup;

        using (Stream? stream = FindAsset(
            GameEnv.DataFolderPath,
            skillGroup.SkydomePath,
            skillGroup.MeshArchive))
        {

            if (stream is not null)
            {
                var nif = new NifFile(stream);
                if (nif.Valid)
                {
                    this.FindControl<SkydomeView>("View")!.LoadSkydome(nif, skillGroup.CameraRightPoint);
                }
            }
        }

        ComboBox skillsCombo = this.FindControl<ComboBox>("SkillCombo")!;
        skillsCombo.ItemsSource = skillGroup.GetSkillList(GameEnv.LinkCache);
        skillsCombo.SelectedIndex = 0;
    }

    private void SkillCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count != 1)
            return;

        var item = e.AddedItems[0] as ISkillInfo;
        if (item is null)
            return;

        if (SelectedGroup is null)
            return;

        int FindIndex()
        {
            int i = 0;
            foreach (var skill in SelectedGroup.GetSkillList(GameEnv.LinkCache))
            {
                if (skill.ActorValueInfo == item.ActorValueInfo)
                {
                    return i;
                }
                ++i;
            }

            return -1;
        }

        var index = FindIndex();
        if (index >= 0)
        {
            this.FindControl<SkydomeView>("View")!.ShowPerks(index, item.ActorValueInfo);
        }
    }

    private void FOV_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        this.FindControl<SkydomeView>("View")!.FOV = (float)e.NewValue;
    }

    private void CameraLookAtZ_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        this.FindControl<SkydomeView>("View")!.SkillsLookAtZ = (float)e.NewValue;
    }
}