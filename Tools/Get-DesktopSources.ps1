# Shared portable-compiler source list. The Desktop project is the sole manifest.
function Get-DesktopSources {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)
    $projectPath = Join-Path $ProjectRoot 'LeiyunLite.Desktop.csproj'
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $ns = New-Object System.Xml.XmlNamespaceManager($project.NameTable)
    $ns.AddNamespace('m', 'http://schemas.microsoft.com/developer/msbuild/2003')
    $sources = @()
    foreach ($item in $project.SelectNodes('/m:Project/m:ItemGroup/m:Compile', $ns)) {
        $include = $item.GetAttribute('Include')
        # Current manifest uses literal paths and nonrecursive directory globs.
        # Reject unsupported MSBuild expressions rather than silently omit files.
        if (-not $include -or $include -match '\$|;|\*\*' -or
            $item.HasAttribute('Exclude') -or $item.HasAttribute('Condition') -or
            $item.ParentNode.HasAttribute('Condition')) {
            throw "Unsupported Compile item in Desktop project: $include"
        }
        $path = Join-Path $ProjectRoot $include
        if ($include -match '[*?]') {
            $matches = @(Get-ChildItem -Path $path -File | Sort-Object FullName)
        } else {
            $matches = @(Get-Item -LiteralPath $path -ErrorAction Stop)
        }
        if ($matches.Count -eq 0) { throw "Compile item matches no source: $include" }
        foreach ($file in $matches) {
            if ($file.PSIsContainer -or $sources -contains $file.FullName) {
                throw "Invalid or duplicate Compile source: $($file.FullName)"
            }
            $sources += $file.FullName
        }
    }
    if ($sources.Count -eq 0) { throw 'No Desktop project sources found.' }
    return $sources
}
