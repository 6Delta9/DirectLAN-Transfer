using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FastEthernetTransfer.Core.Diagnostics;
using FastEthernetTransfer.Core.Models;

namespace FastEthernetTransfer.Core.Storage;

public class HistoryService
{
    private static readonly Lock _lock = new();
    private readonly string _historyFilePath;
    private const int MaxHistoryItems = 100;

    public HistoryService()
    {
        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FastEthernetTransfer");
        Directory.CreateDirectory(appData);
        _historyFilePath = Path.Combine(appData, "history.json");
    }

    public List<TransferHistoryItem> LoadHistory()
    {
        lock (_lock)
        {
            if (!File.Exists(_historyFilePath))
                return [];

            try
            {
                string json = File.ReadAllText(_historyFilePath);
                var items = JsonSerializer.Deserialize<List<TransferHistoryItem>>(json);
                return items ?? [];
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load transfer history", ex);
                return [];
            }
        }
    }

    public void AddItem(TransferHistoryItem item)
    {
        lock (_lock)
        {
            try
            {
                var history = LoadHistory();
                history.Insert(0, item);
                if (history.Count > MaxHistoryItems)
                {
                    history = history.Take(MaxHistoryItems).ToList();
                }

                string json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_historyFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save transfer history item", ex);
            }
        }
    }

    public void ClearHistory()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    File.Delete(_historyFilePath);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to clear transfer history", ex);
            }
        }
    }
}
