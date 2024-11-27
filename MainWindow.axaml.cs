// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using NiflySharp;
using Noggog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PerkTreeEditor;

public partial class MainWindow : Window
{
    private static readonly List<FormLink<IActorValueInformationGetter>> DefaultTrees = [
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
    ];

    private readonly IGameEnvironment<ISkyrimMod, ISkyrimModGetter> GameEnv;
    private readonly Dictionary<string, WeakReference<Texture>> TextureHolder = [];

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

        LoadDefaultSkydome(dataFolder);
        LoadCommonAssets(dataFolder);

        ComboBox skillsCombo = this.FindControl<ComboBox>("SkillCombo")!;
        IEnumerable<string?> ItemsSource()
        {
            foreach (var av in DefaultTrees)
            {
                if (av.TryResolve(GameEnv.LinkCache, out var actorValueInfo))
                {
                    yield return actorValueInfo.Name?.String;
                }
            }
        }

        skillsCombo.ItemsSource = ItemsSource();
        skillsCombo.SelectedIndex = 0;
    }

    private void LoadDefaultSkydome(DirectoryPath dataFolder)
    {
        using Stream? stream = FindAsset(
            dataFolder,
            "Meshes/Interface/INTPerkSkydome.nif",
            "Skyrim - Meshes1.bsa");

        if (stream is not null)
        {
            var nif = new NifFile(stream);
            if (nif.Valid)
            {
                this.FindControl<SkydomeView>("View")!.LoadSkydome(nif);
            }
        }
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

    private static Stream? FindAsset(DirectoryPath dataFolder, string path, string archive)
    {
        var looseFile = new FilePath(Path.Join(dataFolder, path));
        if (looseFile.Exists)
        {
            return looseFile.OpenRead();
        }

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
        GameEnv?.Dispose();
    }

    private void ComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count != 1)
            return;

        var item = e.AddedItems[0] as string;
        if (item is null)
            return;

        for (int i = 0; i < DefaultTrees.Count; ++i)
        {
            if (DefaultTrees[i].TryResolve(GameEnv.LinkCache, out var actorValueInfo))
            {
                if (actorValueInfo.Name?.String == item)
                {
                    this.FindControl<SkydomeView>("View")!.ShowPerks(i, actorValueInfo);
                }
            }
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