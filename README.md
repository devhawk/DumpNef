# DumpNef

Simple command line utility to dump [Neo N3](https://neo.org/blog/details/4225?language=en) smart contract files to the console. 

## Install

``` shell
$ dotnet tool install -g DevHawk.DumpNef
```

## example output

```
$ dumpnef registrar.nef
# Start Method DevHawk.Contracts.Registrar.delete
000 INITSLOT 04-01 # 4 local variables, 1 arguments
003 NOP
004 LDARG0
005 NOP
006 CALL_L 95-00-00-00 # pos: 155, offset: 149
011 STLOC0
012 LDLOC0
013 NOP
014 PUSH0
015 NUMEQUAL
016 STLOC1
017 LDLOC1
... additional lines ommitted
```
