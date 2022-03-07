%        LOC     Data_Segment
%        GREG    @
%InputE  OCTA    0

       LOC Data_Segment
freq   GREG @           Base address for even byte counts
       LOC @+8*(1<<8)   Space for the byte frequencies
freqq  GREG @           Base address for odd byte counts
       LOC @+8*(1<<8)   Space for the byte frequencies
p      GREG @
       BYTE "abracadabraa",0,"abc" Trivial test data
ones   GREG #0101010101010101
       LOC  #100

Main SWYM % () [] = ?
 LDO  $1,freq,$2
 SET $255,0
 TRAP 0,Halt,0

cwrite SWYM
 SET $255,$0
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
