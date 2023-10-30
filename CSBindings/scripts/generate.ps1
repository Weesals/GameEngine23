# Read the contents of the .rsp file
$arguments = "generate.rsp"

# clang -std=c++20 -Xclang -ast-dump -fsyntax-only @$arguments

# Call ClangSharpPInvokeGenerator.exe with the arguments from the .rsp file
& "ClangSharpPInvokeGenerator.exe" @$arguments
