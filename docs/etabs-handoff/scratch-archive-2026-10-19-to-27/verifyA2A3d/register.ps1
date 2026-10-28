$core = 'C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.Core.Tests\Kor.Operations.EngineeringTools.Core.Tests.csproj'
$out  = 'C:\Users\ilalonde\AppData\Local\Temp\claude\C--VIsual-Studio-Projects-Operations\d8312bb3-eb17-42ed-b4ca-1ec6d7f8399a\scratchpad\verifyA2A3d\register.txt'
$filter = 'FullyQualifiedName~EveryReaderConstantIsTriaged|FullyQualifiedName~AMarkupLabelSitsWhereTheSheetDrawsIt|FullyQualifiedName~ADxfOpensOnItsDrawing'
dotnet test $core -c Debug --filter $filter -nologo *> $out
"exit $LASTEXITCODE"
Select-String -Path $out -Pattern 'error CS|Passed!|Failed!|^\s+Failed '
