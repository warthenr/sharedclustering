﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using AncestryDnaClustering.Models.HierarchicalClustering.ColumnWriters;
using AncestryDnaClustering.Models.SavedData;
using AncestryDnaClustering.ViewModels;
using OfficeOpenXml;
using OfficeOpenXml.ConditionalFormatting;

namespace AncestryDnaClustering.Models.HierarchicalClustering.CorrelationWriters
{
    /// <summary>
    /// Write a correlation matrix to an Excel file.
    /// </summary>
    public class ExcelCorrelationWriter : ICorrelationWriter
    {
        private readonly string _correlationFilename;
        private readonly string _testTakerTestId;
        private readonly string _ancestryHostName;
        private readonly int _minClusterSize;
        private readonly double _lowestClusterableCentimorgans;
        private readonly ProgressData _progressData;
        private ExcelPackage _p = null;

        public ExcelCorrelationWriter(string correlationFilename, string testTakerTestId, string ancestryHostName, int minClusterSize, int maxMatchesPerClusterFile, double lowestClusterableCentimorgans, ProgressData progressData)
        {
            _correlationFilename = correlationFilename;
            _testTakerTestId = testTakerTestId;
            _ancestryHostName = ancestryHostName;
            _minClusterSize = minClusterSize;
            _lowestClusterableCentimorgans = lowestClusterableCentimorgans;
            MaxMatchesPerClusterFile = Math.Min(MaxColumns, maxMatchesPerClusterFile);
            _progressData = progressData;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _p?.Dispose();
                _p = null;
            }
        }

        public IDisposable BeginWriting()
        {
            _p = new ExcelPackage();
            return _p;
        }

        private bool FileIsOpen() => _p != null;

        public int MaxColumns => 16000;
        public int MaxMatchesPerClusterFile { get; }

        public async Task<List<string>> OutputCorrelationAsync(
            List<ClusterNode> nodes,
            Dictionary<int, IClusterableMatch> matchesByIndex,
            Dictionary<int, int> indexClusterNumbers,
            List<Tag> tags,
            string worksheetName)
        {
            if (string.IsNullOrEmpty(_correlationFilename))
            {
                return new List<string>();
            }

            if (nodes.Count == 0)
            {
                return new List<string>();
            }

            // All nodes, in order. These will become rows/columns in the Excel file.
            var leafNodes = nodes.First().GetOrderedLeafNodes().ToList();

            // Ancestry never shows matches lower than 20 cM as shared matches.
            // The distant matches will be included as rows in the Excel file, but not as columns.
            // That means that correlation diagrams that include distant matches will be rectangular (tall and narrow)
            // rather than square.
            var matches = leafNodes
                .Where(leafNode => matchesByIndex.ContainsKey(leafNode.Index))
                .Select(leafNode => matchesByIndex[leafNode.Index])
                .ToList();
            var lowestClusterableCentimorgans = matches
                .SelectMany(match => match.Coords.Where(coord => coord != match.Index && matchesByIndex.ContainsKey(coord)))
                .Distinct()
                .Where(coord => matchesByIndex[coord].Match.SharedCentimorgans >= _lowestClusterableCentimorgans)
                .Min(coord => matchesByIndex[coord].Match.SharedCentimorgans);
            var nonDistantMatches = matches
                .Where(match => match.Match.SharedCentimorgans >= lowestClusterableCentimorgans)
                .ToList();

            // Excel has a limit of 16,384 columns.
            // If there are more than 16,000 matches, split into files containing at most 10,000 columns.
            var numOutputFiles = 1;
            if (nonDistantMatches.Count > MaxMatchesPerClusterFile)
            {
                numOutputFiles = (nonDistantMatches.Count - 1) / MaxMatchesPerClusterFile + 1;
            }

            _progressData.Reset("Saving clusters", leafNodes.Count * numOutputFiles);

            var orderedIndexes = nonDistantMatches
                .Select(match => match.Index)
                .ToList();

            // Because very strong matches are included in so many clusters,
            // excluding the strong matches makes it easier to identify edges of the clusters. 
            var immediateFamilyIndexes = new HashSet<int>(
                matchesByIndex.Values
                .Where(match => match.Match.SharedCentimorgans > 200)
                .Select(match => match.Index)
                );

            // Fixed columns
            var clusterNumberWriter = new ClusterNumberWriter(indexClusterNumbers);
            var writers = new List<IColumnWriter>
            {
                clusterNumberWriter,
                new NameWriter(false),
                matches.Any(match => !string.IsNullOrEmpty(match.Match.TestGuid)) ? new TestIdWriter() : null,
                !string.IsNullOrEmpty(_testTakerTestId) ? new LinkWriter(_testTakerTestId, _ancestryHostName) : null,
                new SharedCentimorgansWriter(),
                matches.Any(match => match.Match.SharedSegments > 0) ? new SharedSegmentsWriter() : null,
                matches.Any(match => match.Match.LongestBlock > 0) ? new LongestBlockWriter() : null,
                matches.Any(match => !string.IsNullOrEmpty(match.Match.TreeUrl)) ? new TreeUrlWriter(_testTakerTestId) : null,
                matches.Any(match => match.Match.TreeType != SavedData.TreeType.Undetermined) ? new TreeTypeWriter() : null,
                matches.Any(match => match.Match.TreeSize > 0) ? new TreeSizeWriter() : null,
                matches.Any(match => match.Match.CommonAncestors?.Count > 0) ? new CommonAncestorsWriter() : null,
                matches.Any(match => match.Match.Starred) ? new StarredWriter() : null,
                matches.Any(match => match.Match.HasHint) ? new SharedAncestorHintWriter() : null,
                new CorrelatedClustersWriter(leafNodes, immediateFamilyIndexes, indexClusterNumbers, clusterNumberWriter, _minClusterSize),
            }.Where(writer => writer != null).ToList();
            if (tags != null)
            {
                writers.AddRange(tags.OrderBy(tag => tag.Label).Select(tag => new TagWriter(tag)));
            }
            writers.Add(new NoteWriter());

            if (!FileIsOpen())
            {
                return await OutputFiles(worksheetName, matchesByIndex, leafNodes, nonDistantMatches, orderedIndexes, writers.ToArray(), numOutputFiles);
            }
            else
            {
                await OutputWorksheet(worksheetName, matchesByIndex, leafNodes, nonDistantMatches, orderedIndexes, writers.ToArray(), 0);
                return new List<string>{ _correlationFilename };
            }
        }

        private async Task<List<string>> OutputFiles(
            string worksheetName,
            Dictionary<int, IClusterableMatch> matchesByIndex,
            List<LeafNode> leafNodes,
            List<IClusterableMatch> nonDistantMatches,
            List<int> orderedIndexes,
            IColumnWriter[] writers,
            int numOutputFiles)
        {
            var files = new List<string>();

            for (var fileNum = 0; fileNum < numOutputFiles; ++fileNum)
            {
                using (var p = BeginWriting())
                {
                    await OutputWorksheet(worksheetName, matchesByIndex, leafNodes, nonDistantMatches, orderedIndexes, writers, fileNum);
                    files.Add(SaveFile(fileNum));
                }
                _p = null;
            }
            return files;
        }

        public string SaveFile(int fileNum)
        {
            var fileName = _correlationFilename;
            if (fileNum > 0)
            {
                fileName = FileUtils.AddSuffixToFilename(fileName, (fileNum + 1).ToString());
            }

            FileUtils.Save(_p, fileName);

            return fileName;
        }

        private Task OutputWorksheet(
            string worksheetName,
            Dictionary<int, IClusterableMatch> matchesByIndex,
            List<LeafNode> leafNodes,
            List<IClusterableMatch> nonDistantMatches,
            List<int> orderedIndexes,
            IColumnWriter[] writers,
            int fileNum)
        {
            return Task.Run(() =>
            {
                var ws = _p.Workbook.Worksheets.Add(worksheetName);

                // Start at the top left of the sheet
                var row = 1;
                var col = 1;

                // Rotate the entire top row by 90 degrees
                ws.Row(row).Style.TextRotation = 90;

                var columnWriters = new ColumnWritersCollection(_p, ws, writers, _testTakerTestId);

                col = columnWriters.WriteHeaders(row, col);

                var firstMatrixDataRow = row + 1;
                var firstMatrixDataColumn = col;

                // Column headers for each match
                var matchColumns = nonDistantMatches.Skip(fileNum * MaxMatchesPerClusterFile).Take(MaxMatchesPerClusterFile).ToList();
                foreach (var nonDistantMatch in matchColumns)
                {
                    ws.Cells[row, col++].Value = nonDistantMatch.Match.Name;
                }

                // One row for each match
                foreach (var leafNode in leafNodes)
                {
                    var match = matchesByIndex[leafNode.Index];
                    row++;

                    // Row headers
                    col = 1;
                    col = columnWriters.WriteColumns(row, col, match, leafNode);

                    // Correlation data
                    foreach (var coordAndIndex in leafNode.GetCoordsArray(orderedIndexes)
                        .Zip(orderedIndexes, (c, i) => new { Coord = c, Index = i })
                        .Skip(fileNum * MaxMatchesPerClusterFile).Take(MaxMatchesPerClusterFile))
                    {
                        if (coordAndIndex.Coord != 0)
                        {
                            ws.Cells[row, col].Value = coordAndIndex.Coord;
                        }
                        col++;
                    }

                    _progressData.Increment();
                }

                if (leafNodes.Count > 0 && matchColumns.Count > 0)
                {
                    // Heatmap color scale
                    var correlationData = new ExcelAddress(firstMatrixDataRow, firstMatrixDataColumn, firstMatrixDataRow - 1 + leafNodes.Count, firstMatrixDataColumn - 1 + matchColumns.Count);
                    var threeColorScale = ws.ConditionalFormatting.AddThreeColorScale(correlationData);
                    threeColorScale.LowValue.Type = eExcelConditionalFormattingValueObjectType.Num;
                    threeColorScale.LowValue.Value = 0;
                    threeColorScale.LowValue.Color = Color.Gainsboro;
                    threeColorScale.MiddleValue.Type = eExcelConditionalFormattingValueObjectType.Num;
                    threeColorScale.MiddleValue.Value = 1;
                    threeColorScale.MiddleValue.Color = Color.Cornsilk;
                    threeColorScale.HighValue.Type = eExcelConditionalFormattingValueObjectType.Num;
                    threeColorScale.HighValue.Value = 2;
                    threeColorScale.HighValue.Color = Color.DarkRed;
                }

                // Heatmap number format
                ws.Cells[$"1:{matchColumns.Count}"].Style.Numberformat.Format = "General";

                col = 1;
                col = columnWriters.FormatColumns(row, col, firstMatrixDataRow + leafNodes.Count);

                // Freeze the column and row headers
                ws.View.FreezePanes(firstMatrixDataRow, firstMatrixDataColumn);
            });
        }
    }
}
