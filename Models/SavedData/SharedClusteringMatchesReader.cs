﻿using System.IO;
using System.Threading.Tasks;
using AncestryDnaClustering.ViewModels;

namespace AncestryDnaClustering.Models.SavedData
{
    /// <summary>
    /// Read files saved natively by Shared Clustering.
    /// </summary>
    public class SharedClusteringMatchesReader : ISerializedMatchesReader
    {
        public bool IsSupportedFileType(string fileName) => fileName != null && Path.GetExtension(fileName).ToLower() == ".txt";

        public string GetTrimmedFileName(string fileName) => IsSupportedFileType(fileName) ? Path.GetFileNameWithoutExtension(fileName) : null;

        public async Task<(Serialized input, string errorMessage)> ReadFileAsync(string fileName, ProgressData progressData)
        {
            if (!IsSupportedFileType(fileName))
            {
                return (null, $"{fileName} is not a *.txt file");
            }

            var serialized = await Task.Run(() => FileUtils.ReadAsJson<Serialized>(fileName, false, false));
            return serialized != null 
                ? (serialized, (string)null)
                : (null, $"Unable to read file {fileName}");
        }
    }
}
