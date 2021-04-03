# DumpNef

Simple command line utility to dump [Neo N3](https://neo.org/blog/details/4225?language=en) smart contract files to the console. 

## Install

> Note, this tool has not been uploaded to nuget.org yet. In the meantime, you must clone and build locally

``` shell
$ dotnet tool install -g DevHawk.DumpNef
```

## Example Output

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

By default, dumpnef color codes the Neo VM instructions. This can be disabled with the `--disable-colors` option.

### Colored Output:
![image](https://user-images.githubusercontent.com/8965/113462318-269be480-93d5-11eb-889a-8a38cce54beb.png)

### Monochrome Output:
![image](https://user-images.githubusercontent.com/8965/113462389-737fbb00-93d5-11eb-8528-b29746012c04.png)

