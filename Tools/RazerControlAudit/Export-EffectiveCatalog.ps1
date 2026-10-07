param([string]$AssemblyPath='bin/Desktop1.3.0-final-20261006-r1/LeiyunLite.Desktop.exe',[string]$OutputDirectory='test-artifacts/v130-control-path-audit',[string]$CatalogType='DeviceCapabilityCatalog')
$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::LoadFile((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags=[Reflection.BindingFlags]'NonPublic,Public,Static'
$all=@($assembly.GetType('RazerBatteryTray.'+$CatalogType).GetProperty('All',$flags).GetValue($null,$null))
$rows=@()
foreach($profile in $all){
 $row=[ordered]@{}
 foreach($field in $profile.GetType().GetFields([Reflection.BindingFlags]'NonPublic,Public,Instance')){
  $value=$field.GetValue($profile)
  if($field.FieldType.IsEnum){$value=$value.ToString()}
  $row[$field.Name]=$value
 }
 $row['PID']='{0:X4}'-f $row.ProductId
 $row['FeatureReportLengths']=if($row.DescriptorPolicy-eq 'NagaV3WindowsControl'){'91'}elseif($row.Transport-eq 'HidFeature90Or91'){'90/91'}else{'NOT_MAPPED'}
 $resolver=$assembly.GetType('RazerBatteryTray.RazerControlPathResolver')
 $audited=$resolver -and $resolver.GetMethod('HasAuditedDesktopConsumerCandidates',$flags).Invoke($null,@([int]$row.ProductId))
 $row['AllowedShape']=if($audited){'90/91 vendor or 01/0C any Usage; unique live lock; MI preference'}elseif($row.DescriptorPolicy-eq 'NagaV3WindowsControl'){'01:01/02/03'}elseif($row.RequiredInterfaceNumber-ge 0){'MI_'+$row.RequiredInterfaceNumber+'; old CanProbe or audited consumer usage'}else{'old CanProbe: vendor or 01:01/02'}
 $rows+=[PSCustomObject]$row
}
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
$rows|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $OutputDirectory 'effective-catalog.json') -Encoding UTF8
$flat=foreach($row in $rows){$record=[ordered]@{};foreach($prop in $row.PSObject.Properties){$record[$prop.Name]=if($prop.Value-is [Array]){$prop.Value-join ','}else{$prop.Value}};[PSCustomObject]$record}
$flat|Export-Csv -LiteralPath (Join-Path $OutputDirectory 'effective-catalog.csv') -NoTypeInformation -Encoding UTF8
'Dynamic effective PID count: '+$all.Count
