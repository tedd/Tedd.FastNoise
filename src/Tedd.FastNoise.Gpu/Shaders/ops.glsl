// GPU equivalents of the scalar arithmetic primitives. Explicit temporaries prohibit FMA contraction.
float F(float a) { return a; }
int I(int a) { return a; }
float Add(float a, float b) { precise float r = a + b; return r; }
float Sub(float a, float b) { precise float r = a - b; return r; }
float Mul(float a, float b) { precise float r = a * b; return r; }
float Neg(float a) { return -a; }
float Abs(float a) { return abs(a); }
float Min(float a, float b) { return min(a,b); }
float ToFloat(int a) { return float(a); }
int ToInt(float a) { return int(a); }
int AddI(int a,int b) { return int(uint(a)+uint(b)); }
int SubI(int a,int b) { return int(uint(a)-uint(b)); }
int MulI(int a,int b) { return int(uint(a)*uint(b)); }
int XorI(int a,int b) { return a^b; }
int AndI(int a,int b) { return a&b; }
int OrI(int a,int b) { return a|b; }
int NotI(int a) { return ~a; }
int ShiftLeft(int a,int b) { return int(uint(a)<<b); }
int ShiftRightArithmetic(int a,int b) { return a>>b; }
int LessThanF(float a,float b) { return a<b ? -1:0; }
int GreaterThanF(float a,float b) { return a>b ? -1:0; }
float SelectF(int m,float a,float b) { return m!=0 ? a:b; }
int SelectI(int m,int a,int b) { return m!=0 ? a:b; }
