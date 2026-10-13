using System;
using System.IO;
using System.Text.Json;
using FastEthernetTransfer.Core.Diagnostics;
using FastEthernetTransfer.Core.Models;

namespace FastEthernetTransfer.Core.Storage;

public class SettingsService
{
    private readonly string _settingsFilePath;
    private AppSettings _settings;
    private readonly Lock _lock = new();

    public AppSettings Settings => _settings;

    public SettingsService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FastEthernetTransfer");
        Directory.CreateDirectory(dir);
        _settingsFilePath = Path.Combine(dir, "settings.json");
        _settings = Load();
    }

    public AppSettings Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load settings file, using defaults", ex);
            }

            var defaultSettings = new AppSettings();
            SaveInternal(defaultSettings);
            return defaultSettings;
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            _settings = settings;
            SaveInternal(settings);
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            SaveInternal(_settings);
        }
    }

    private void SaveInternal(AppSettings settings)
    {
        try
        {
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save settings file", ex);
        }
    }

    public void AddTrustedPeer(Guid peerId)
    {
        lock (_lock)
        {
            _settings.TrustedPeerIds.Add(peerId);
            SaveInternal(_settings);
        }
    }

    public bool IsPeerTrusted(Guid peerId)
    {
        lock (_lock)
        {
            return _settings.TrustedPeerIds.Contains(peerId);
        }
    }
}
