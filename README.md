# DumpNef

Simple command line utility to dump [Neo N3](https://neo.org/blog/details/4225?language=en) smart contract files to the console. 

## Install

> Note, this tool has not been uploaded to nuget.org yet. In the meantime, you must clone and build locally

``` shell
$ dotnet tool install -g DevHawk.DumpNef
```

## Example Output

```
$ dumpnef .\registrar.nef --disable-colors
# Method Start DevHawk.Contracts.Registrar.delete
000 INITSLOT 04-01 # 4 local variables, 1 arguments
# Code Registrar.cs line 73: "{"
003 NOP
# Code Registrar.cs line 74: "var currentOwner = GetDomainOwner(domain);"
004 LDARG0
005 NOP
006 CALL_L 95-00-00-00 # pos: 155, offset: 149
011 STLOC0
# Code Registrar.cs line 75: "if (currentOwner.IsZero)"
012 LDLOC0
013 NOP
014 PUSH0
015 NUMEQUAL
016 STLOC1
017 LDLOC1
018 JMPIFNOT_L 2B-00-00-00 # pos: 61, offset: 43
... additional lines ommitted
```

By default, dumpnef color codes the Neo VM instructions. This can be disabled with the `--disable-colors` option.

### Colored Output:
![image](https://user-images.githubusercontent.com/8965/115155419-c9d73580-a034-11eb-8b10-5acc5c3ba24b.png)


### Monochrome Output:
![image](https://user-images.githubusercontent.com/8965/115155470-f12e0280-a034-11eb-88f0-973eaf2c690f.png)


