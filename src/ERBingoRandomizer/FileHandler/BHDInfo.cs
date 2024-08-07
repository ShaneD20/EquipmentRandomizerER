﻿using SoulsFormats;
using System.IO;

namespace Project.FileHandler;

public class BHDInfo
{
    private readonly string _bdtPath;
    private readonly BHD5 _bhd;
    private string _bhdPath;

    public BHDInfo(BHD5 bhd, string bdt)
    {
        _bhd = bhd;
        _bdtPath = $"{bdt}.bdt";
        _bhdPath = $"{bdt}.bhd";
    }
    public string GetSalt()
    {
        return _bhd.Salt;
    }
    public byte[]? GetFile(ulong hash)
    {
        BHD5.Bucket bucket = _bhd.Buckets[(int)(hash % (ulong)_bhd.Buckets.Count)];

        foreach (BHD5.FileHeader header in bucket)
        {
            if (header.FileNameHash != hash) { continue; }

            using FileStream fs = new(_bdtPath, FileMode.Open);
            return header.ReadFile(fs);
        }
        return null;
    }
}
