#!/usr/bin/env pwsh

# Script to add clear timestamp debug output

$filePath = "d:\devgit\joe\sensenet-index-tools\src\MainProgram\ContentComparer.cs"

# First, let's make a backup of the original file if one doesn't exist
if (-not (Test-Path "$filePath.bak")) {
    Copy-Item $filePath "$filePath.bak" -Force
}

# Read the current content
$content = Get-Content $filePath -Raw

# Update the ToString method to include the numeric timestamp value
if ($content -match 'public override string ToString\(\)\s*{\s*return \$"\{.*?\}";') {
    $oldToString = $Matches[0]
    $newToString = 'public override string ToString()
            {
                return $"{(InDatabase ? NodeId.ToString() : "-")}\t{(InDatabase ? VersionId.ToString() : "-")}\t" +
                       $"{(InDatabase ? Timestamp.ToString("yyyy-MM-dd HH:mm:ss") : "-")}\t{(InDatabase ? TimestampNumeric.ToString() : "-")}\t" +
                       $"{(InIndex ? IndexNodeId : "-")}\t{(InIndex ? IndexVersionId : "-")}\t" +
                       $"{(InIndex ? IndexTimestamp : "-")}\t" +
                       $"{Path}\t{NodeType}\t{Status}";
            }'
    
    $content = $content.Replace($oldToString, $newToString)
}

# Update the header line that's printed to include the numeric timestamp
$headerPattern = 'Console\.WriteLine\("\\nDB_NodeId\\tDB_VerID\\tDB_Timestamp\\tIdx_NodeId\\tIdx_VerID\\tIdx_Timestamp\\tPath\\tNodeType\\tStatus"\);'
$newHeader = 'Console.WriteLine("\nDB_NodeId\tDB_VerID\tDB_Timestamp\tDB_TimestampNumeric\tIdx_NodeId\tIdx_VerID\tIdx_Timestamp\tPath\tNodeType\tStatus");'
$content = $content -replace $headerPattern, $newHeader

# Write the updated content back to the file
$content | Set-Content $filePath -Force

Write-Host "File updated to show numeric timestamp values in output."
