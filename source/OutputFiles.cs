namespace GameplayRecorder;

public static class OutputFiles
{
    public static string Available(string requested,params string[] protectedPaths)
    {
        string full=Path.GetFullPath(requested),parent=Path.GetDirectoryName(full)!,stem=Path.GetFileNameWithoutExtension(full),extension=Path.GetExtension(full);
        var numbered=System.Text.RegularExpressions.Regex.Match(stem,@"^(.*) \((\d+)\)$");int first=1;
        if(numbered.Success&&int.TryParse(numbered.Groups[2].Value,out int existing)&&existing<1000000){stem=numbered.Groups[1].Value;first=existing+1;}
        var excluded=protectedPaths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for(int number=0;number<10000;number++){
            string candidate=number==0?full:Path.Combine(parent,$"{stem} ({first+number-1}){extension}");
            if(!excluded.Contains(candidate)&&!File.Exists(candidate)&&!Directory.Exists(candidate))return candidate;
        }
        throw new IOException("Too many files have this name. Choose a different filename.");
    }
    // No overwrite, including a collision that occurs after encoding has finished.
    public static string Commit(string pending,string requested,bool autoRename,params string[] protectedPaths)
    {
        while(true){
            string output=autoRename?Available(requested,protectedPaths):Path.GetFullPath(requested);
            try{File.Move(pending,output,false);return output;}
            catch(IOException) when(autoRename&&(File.Exists(output)||Directory.Exists(output))){ }
        }
    }
}
