 LOC #100
Main SWYM % () [] = ?
 SET $2,42
 SET $3,$2
 PUSHJ $2,Dummy
 SET $2,32
 SET $3,1
 SET $4,$3
 SET $3,$2
 PUSHJ $2,newarr
 SET $0,$2
 SET $2,$0
 SET $3,0
 SET $1,$2
 SET $2,$1
 SET $3,$0
 SUB $3,$3,8
 LDO $3,$3
 SET $4,$3
 SET $3,$2
 PUSHJ $2,cread
 SET $2,$1
 SET $3,$2
 PUSHJ $2,cwrite
 SET $2,0
 SET $1,$2
 SET $2,$0
 SET $3,$2
 PUSHJ $2,delarr
 SET $255,0
 TRAP 0,Halt,0

Dummy SWYM % () [] = ?
 POP

cread SET $255,$0
 TRAP 0,Fgets,StdIn
 POP 1,0
cwrite SWYM
 GET $255,rO
 TRAP 0,Fputs,StdOut
 POP
malloc SWYM % () [] = ?
 SET $1,$0
 SET $3,0
 SET $2,$3
 JMP malloc_6
malloc_6 SET $3,$2
 SET $0,$3
 POP 1,0

free SWYM % () [] = ?
 POP

newarr SWYM
 GET $2,rJ
 MUL $4,$0,$1
 ADD $4,$4,8
 PUSHJ $3,malloc
 ADD $0,$3,8
 PUT rJ,$2
 POP 1,0
delarr SWYM
 GET $2,rJ
 SUB $4,$4,8
 PUSHJ $3,free
 SET $0,$3
 PUT rJ,$2
 POP
