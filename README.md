# file_hasher
Hashes all files in the specified folders and sub-folders.

# Help

Display help by running 'dotnet file_hasher.dll -h' or 'dotnet file_hasher.dll --help'

```
NAME
    file_hasher

SYNOPSIS
    file_hasher [-a=<algorithm>] [-c] [-e] [-m=<thread count>] [-s] -i=<input folder 1>,<input folder 2>
    [-d=<duplicate output file>] [-o=<output file>]

DESCRIPTION
    Hashes all the files found in a folder (and subfolders) and looks for duplicates.

    a,algorithm=HashAlgorithm
        [Optional] - Algorithm to use for hashing the files. Options are SHA256 or MD5. Defaults to SHA256 if not
        provided.
    c,compressed
        [Optional] - Also hashes files inside zip and gz archives by decompressing them on the fly. The archive
        itself is still hashed.
    d,dup=DuplicateFilePath
        [Optional] - Tracks duplicate files and outputs them to the specified file.
    h,help
        [Optional] - Displays this help page and exits the program.
    i,input=InputFolders,...
        [Optional] - Folders containing files and sub-folders of files to be hashed.
    m,threads=ThreadCount
        [Optional] - Number of threads to use when scanning and hashing. Defaults to 1 if not provided.
    o,output=OutputPath
        [Optional] - Creates a csv file containing all the files found and their hash.
    e,error
        [Optional] - Displays all files and folders that caused an error when accessed. This is most likely due to
        inaccessibility.
    s,symlinks
        [Optional] - Processes symbolic links as if they were normal files. If not specified, then symbolic links to
        files and folders are ignored.
    t,type=HashFormatString
        [Optional] - Determines the format of the hash. Options are 'hex' or 'base64'. Defaults to 'hex' if not
        provided.
```
