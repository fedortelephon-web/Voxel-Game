Get-ChildItem -Path "C:\DevVoxelGame\VoxelGame\Assets\Scripts" -Recurse -Filter "*.cs" | ForEach-Object {
    [PSCustomObject]@{
        File = $_.FullName
        Lines = (Get-Content $_.FullName | Measure-Object -Line).Lines
    }
} | Sort-Object Lines | Select-Object -First 1