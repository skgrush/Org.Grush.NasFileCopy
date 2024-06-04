using System.Collections.Specialized;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Org.Grush.NasFileCopy.ServerSide.Config;

namespace Org.Grush.NasFileCopy.ServerSide.SystemCom.macos;


public class MacosLsblkService : LsblkService
{
  public override async Task<bool> ReadLsblk()
  {
    await Task.CompletedTask;
    throw new NotImplementedException();
  }

  // private async Task<> ReadDiskutilList()
  // {
  //   var process = new Process();
  //
  //   process.StartInfo.WorkingDirectory = "/usr/sbin";
  //   process.StartInfo.FileName = "diskutil";
  //   process.StartInfo.Arguments = "list";
  //
  //   process.StartInfo.UseShellExecute = false;
  //   process.StartInfo.CreateNoWindow = true;
  //   process.StartInfo.RedirectStandardOutput = true;
  //   process.Start();
  //
  //   var outputString = await process.StandardOutput.ReadToEndAsync();
  //
  //   await process.WaitForExitAsync();
  //
  //   var segments = outputString.Split("\n\n", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
  //
  //
  // }
  //
  // private object ReadDiskutilListSegment(string segment)
  // {
  //   var parts = segment.Split('\n');
  //   var firstLine = parts[0];
  //
  //   var children = new List<object>();
  //   if (parts.Length > 1 && parts[1].Contains('#'))
  //   {
  //     var headerLine = parts[1];
  //     var headerDict = ColumnSplitter(headerLine);
  //     var rawLines = parts[2..];
  //
  //     foreach (var line in rawLines)
  //     {
  //       var numberString = ReadFromLine(line, headerDict, "#:");
  //
  //       var entry = new RawDistUtilEntry(
  //         Number: int.Parse(numberString.Trim()[..^1]),
  //         Type:
  //       );
  //     }
  //   }
  // }
  //
  // private record RawDistUtilEntry(int Number, string Type, string Name, string Size, string Identifier);
  //
  // private string ReadFromLine(string line, Dictionary<string, (int startIdx, int endIdx)> headerDict, string column)
  // {
  //   var (start, end) = headerDict[column];
  //   return line.Substring(start, end - start);
  // }
  //
  // private Dictionary<string, (int startIdx, int endIdx)> ColumnSplitter(string headerLine)
  // {
  //   var whitespaceCounter = new Regex(@"\s\s+(?=\S)");
  //
  //   var dict = new Dictionary<string, (int startIdx, int endIdx)>();
  //
  //   var matches = whitespaceCounter.Matches(headerLine);
  //
  //   for (var i = 0; i < matches.Count; ++i)
  //   {
  //     var match = matches[i];
  //     var precedingWhitespaceLength = match.Length;
  //     var headerIdx = match.Index + precedingWhitespaceLength;
  //
  //     var nextIdx = i + 1 == matches.Count ? headerLine.Length : matches[i + 1].Index;
  //
  //     var headerText = headerLine.Substring(headerIdx, nextIdx - headerIdx);
  //
  //     dict[headerText] = (headerIdx, nextIdx);
  //   }
  //
  //   return dict;
  // }
}
