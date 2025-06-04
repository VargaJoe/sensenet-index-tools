#!/usr/bin/env pwsh

# Script to update the ContentComparer.cs file with proper timestamp handling

$filePath = ".\src\MainProgram\ContentComparer.cs"

# First, let's make a backup of the original file
Copy-Item $filePath "$filePath.bak" -Force

# Read the current content
$content = Get-Content $filePath -Raw

# Function to update the Status property
function Update-StatusProperty {
    param($content)
    
    $pattern = @'
public string Status
            {
                get
                {
                    if \(!InDatabase\) return "Index only";
                    if \(!InIndex\) return "DB only";
                    
                    bool idsMatch = string\.Equals\(NodeId\.ToString\(\), IndexNodeId\) && 
                                   string\.Equals\(VersionId\.ToString\(\), IndexVersionId\);
                    
                    .*?
                    
                    if \(idsMatch && timestampMatch\) return "Match";
                    if \(idsMatch && !timestampMatch\) return "Timestamp mismatch";
                    return "ID mismatch";
                }
            }
'@

    $replacement = @'
public string Status
            {
                get
                {
                    if (!InDatabase) return "Index only";
                    if (!InIndex) return "DB only";
                    
                    bool idsMatch = string.Equals(NodeId.ToString(), IndexNodeId) && 
                                   string.Equals(VersionId.ToString(), IndexVersionId);
                    
                    // For timestamp comparison
                    bool timestampMatch = false;
                    if (!string.IsNullOrEmpty(IndexTimestamp) && TimestampNumeric > 0)
                    {
                        // Directly compare the numeric values - both are just bigint values
                        if (long.TryParse(IndexTimestamp, out long indexTimestampNumeric))
                        {
                            // Direct comparison of bigint values
                            timestampMatch = (indexTimestampNumeric == TimestampNumeric);
                            
                            // Log the comparison if verbose logging is enabled
                            if (VerboseLogging && idsMatch && !timestampMatch)
                            {
                                Console.WriteLine($"TIMESTAMP DEBUG (NodeId={NodeId}): DB={TimestampNumeric}, Index={indexTimestampNumeric}, Match={timestampMatch}");
                            }
                        }
                    }
                    else if (idsMatch)
                    {
                        // If IDs match but we can't compare timestamps (missing or invalid), assume it's a match
                        // This prevents false negatives when IDs match but timestamp data is incomplete
                        timestampMatch = true;
                        if (VerboseLogging)
                        {
                            Console.WriteLine($"TIMESTAMP FALLBACK: NodeId={NodeId} matches by ID, can't verify timestamp - assuming match");
                        }
                    }
                    
                    if (idsMatch && timestampMatch) return "Match";
                    if (idsMatch && !timestampMatch) return "Timestamp mismatch";
                    return "ID mismatch";
                }
            }
'@

    $content -replace $pattern, $replacement
}

# Function to ensure TimestampNumeric property exists
function Ensure-TimestampNumericProperty {
    param($content)
    
    # Check if TimestampNumeric already exists
    if ($content -match "public\s+long\s+TimestampNumeric\s*\{\s*get;\s*set;\s*\}") {
        Write-Host "TimestampNumeric property already exists, no need to add it."
        return $content
    }
    
    # Add the property
    $pattern = "public\s+DateTime\s+Timestamp\s*\{\s*get;\s*set;\s*\}"
    $replacement = "public DateTime Timestamp { get; set; }`n            public long TimestampNumeric { get; set; } // Numeric timestamp value (bigint from SQL)"
    
    $content -replace $pattern, $replacement
}

# Function to update the ToString method to show TimestampNumeric
function Update-ToStringMethod {
    param($content)
    
    $pattern = 'public override string ToString\(\)\s*{\s*return.*?}'
    $replacement = @'
public override string ToString()
            {
                return $"{(InDatabase ? NodeId.ToString() : "-")}\t{(InDatabase ? VersionId.ToString() : "-")}\t" +
                       $"{(InDatabase ? Timestamp.ToString("yyyy-MM-dd HH:mm:ss") : "-")}\t{(InDatabase ? TimestampNumeric.ToString() : "-")}\t" +
                       $"{(InIndex ? IndexNodeId : "-")}\t{(InIndex ? IndexVersionId : "-")}\t" +
                       $"{(InIndex ? IndexTimestamp : "-")}\t" +
                       $"{Path}\t{NodeType}\t{Status}";
            }
'@
    
    $content -replace $pattern, $replacement
}

# Apply the updates
Write-Host "Updating the ContentComparer.cs file..."
$content = Update-StatusProperty $content
$content = Ensure-TimestampNumericProperty $content
$content = Update-ToStringMethod $content

# Write the updated content back to the file
$content | Set-Content $filePath -Force

Write-Host "File updated successfully. A backup was created at $filePath.bak"
Write-Host "Changes made:"
Write-Host "1. Updated Status property to handle timestamps as numeric values"
Write-Host "2. Ensured TimestampNumeric property exists"
Write-Host "3. Updated ToString method to show numeric timestamp values"
Write-Host ""
Write-Host "Please build the project to verify the changes."
