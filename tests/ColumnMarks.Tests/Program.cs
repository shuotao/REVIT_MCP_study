using RevitMCP.Core;
void Check(bool value) { if(!value) throw new Exception("Matching assertion failed"); }
Check(ColumnMarkMatching.Select(new double[]{},2000,100)==-1);
Check(ColumnMarkMatching.Select(new[]{2001.0},2000,100)==-1);
Check(ColumnMarkMatching.Select(new[]{2000.0},2000,100)==0);
Check(ColumnMarkMatching.Select(new[]{500.0,510.0},2000,100)==-2);
Check(ColumnMarkMatching.Select(new[]{500.0,500.0},2000,100)==-2);
Check(ColumnMarkMatching.Select(new[]{900.0,500.0,1200.0},2000,100)==1);
Check(ColumnMarkMatching.ReusedLabels(new[]{1,2,1,-1,-1}).SetEquals(new[]{1}));
Console.WriteLine("7 matching checks passed.");
