using System;
using Godot;

/// <summary>
/// The noise the water shader reads, carried in the source.
///
/// `base_material_water` binds Resources/shader/noise.dds as its first
/// texture unit (general.material:414-445) and `water_ps` samples it to
/// bend the surface normal (general.hlsl:298-302). Without it there is
/// no ripple, so it has to reach the phone somehow, and a loose PNG in
/// the project would need Godot's import step to have run - which the
/// headless export used to build the APK does not do. Embedded, it is
/// simply there.
///
/// Halved from the original 256 to 128 on each side. The noise is
/// sampled across roughly 83 world units per tile on one axis and 33 on
/// the other, at a render height of 432 pixels; the second half of the
/// detail was never going to reach a screen.
/// </summary>
public static class WaterNoise
{
    const string Png =
        "iVBORw0KGgoAAAANSUhEUgAAAIAAAACACAIAAABMXPacAACJQklEQVR42iT757J1a3YY5o0x3jDzXHmvHb90Up9uNACCJCySpkSK" +
        "gkjRkk0XbUqUfRsukURoUC5fhS/E/60qu1wuyRAANrpP+tKOK6+Z55uGf+g+ngf/r/93u9niFt0qkvqEizNEOVYKipYvrM1HW6P+" +
        "jRLBwJUJ1UxUDA7gNUH/cdxpHNbqkxivznR5wo9l2K18lsi8E884zr1WQHc5favdXzVqVvjXi3Ho5b4V90CvJ+GZwudZ/+6Yz2y4" +
        "1eNPkWoNLQ1+fMH3yXhzK37uOBrgeRslffif0jDX9BCZuwn+3lZsP1E9sh5CnhLnOMkDBDQXLK8HeaTxGPUjHgYyDBfn8LlErXEC" +
        "7H5rB896pv7Z5X5v9f/jVJqrEJV40xBu/XbP5UqcL+AhN97z3XM0RvyoTfpCNgrxt7gcxeVOOAUhZdA8IvzN0Xkbsr3IjvhthvU3" +
        "YqghfYQnDs0afq9AGfglpuUTT8gXuW+k4AtnG1E8gNfYX4D42//Fr0BBWNtRBYiwuA1VR/bHoHsuG5dHflDiZSA1QQsQ3dtqb+dA" +
        "z3vjtuPSQtTBJvJDBKUR2hIiOQJ78mAYGVagygiLgZsYX81N3cjQCO9J3rmssCLhfWHc1F5NnFBcdZHPQsUwSJp94aKLPiTeHpJt" +
        "AuEMyYCQYRRhNOPngPWBxIhJSgVxIYJFUl14ZXtgfO/jiQ4fJPVbxhF8w+relD00I7xnf1vz3Sws2Lx8VGetVI9uE7qaq5/GK8mL" +
        "QvQ9tS3Xvc8jiR00vYdXIdMy/yz7BtYKozxQESqH1XtsXGh7yyNkmbqTmH+wnxI8DXzpcHZLkHM8YB1wz/RFZxWwMJDX/ngJ1SUv" +
        "pRWxF3/7//xveMC5V+bdWV6MEuSPqcUL1EolAib12O54JNIeXgiq0a9IwOgfextHYoI4VCbdh2YqVSaiUo5tsK3vIWRGSCnSAa8t" +
        "IIBqQJxIG6i1mFu3mJgXLfz7KB2UWBpCqPaZ2cu0ph3j3dzNZqNLzQvBvsf0OTrdegxYtEREaSc20o9XIbrmLBPJZbASox5sQVnt" +
        "wm9BjXKoaHuP8ArGhAdNPJXwYtX7jgu5+CZZCt8f0TYAHn46m1UNrWWeq2+vqKvZ9rBnQAuFkY0L00ROYvkTd8LRlOWq4UXvLYlf" +
        "78Edwvg89EejW3jlZH8MaRPklt1buXoHS+GPGi1jU1EUw+LNmE3G01QUwa9hONw5uRz2IRL/0T/58yEKX88tOvnZ89AQN1SZUKCM" +
        "5+RQGU/egG0D92FcKWd9+9TPpBCvU4zoYXBflTq9itTXdEr8C5lEq0445SiuYJHTNCO3DxJ5knnLyDUsLsbM871VhzJ8yeC9PHaq" +
        "raQnngBOlza77GKHtZUPPT8ZN1F06eSPmXkVERRBpEHMbMidnHicuxTgGVVECGeQHsUV2EBPT3QxA3J09BxN6bk2j4O5kfqGKDVe" +
        "KDSJ6lFUTTjFFCmSdfjyWnCGDy2Jw+gNdynNcmEzSEcajtw1oZgqLzFHvNZ29yw/Dm57rtUxcCHWr/KOMG8gIijRv1JoIhzORLWo" +
        "GUeAd7lbR8PIovZRlpgJjNbqzab0m0j8w1/+ecriYuQPP8TZPnqWBhrywIWT/hPwc4A2WIEjkSTqD2Y/2MkiWc2iSYp/acwcxc8u" +
        "BWhxfIb6yZkMVCl2eY9ACumdE82zi1zIBeOLcaeQl7ji4Wy1OWoVc1AwgfBQ2F6FeRniIswG0A3VVpttevTBzcbkxraGYo1UhsJi" +
        "PjUm8q0MqeRTLfWoWmA2YgS6zoZMu9/GafTGJxF+7mA60OFkt+1w8TaRr6IIKetDWQ3bJ28rLwrl76LHNEy1/GJ0T0+hrsOQUOkx" +
        "jUQe0z6EkZhOrEqhpUAJd4vASB8rWY/uaTUs5+XyJqE57iJbXms/0gpCZlw4UgCKkUdAn0LeQPEdxzsevdJHTD979xA9+3g4gfiv" +
        "f/4n0Yqao/j8Yi8+c9jh0Tn5xKlTtkROSCnCgdvec2WqQsRSLD0WCQ5D+Bz88iJZ7e24tT+dfT5AHumh98PgaxwvISUpSFHJnKF/" +
        "1slbbtM8BIeJcbs3iisxAQ+DMJ2kmwFS3xshBpo1uH/SzQaLLooWoRrBi1AUwTLOFgMC9E2EMZ8byTqkgEbCmPv5xM8uhjroREGd" +
        "wUsrjOCtc3Tk7EItZ7LmwFpUDeLDGMawyrhI4XTCLgPxRrhWDGevCVMpxkxQ48WnMXa0T0FEmIyiVFi8cUB4+lE+Pw75gGYt7Ion" +
        "RylOkMxpnIZdxl9MyHiaRHYo1NAjHBgdWkVlHjJ2phKsUGp2R2j34rEG8a//9Z8wwN+04DR3GcR79k0QK7USSiKKEvtE7A0Yw7NE" +
        "Rgn9ELm7BtfIh1TCTHFMYwPR1piJmnyZtIJrEQbrEDCJ1bQWtnEpQNS6fBHwjSpPo4sE7pz6HPaXeiKCkzyZueqs/UucXXX11LW7" +
        "aPcsJhHPUg73+uxhUlJAiLW/I3+fcpz4ypAFQEujE4WnILjMbdZRO+rDsxh+K4wF9jhehWENl7HygkcENNhdg/pKT5eRjskcQ12H" +
        "N5k6TrApiBeqB1RtwKOpQ8inairJR9jEOCnw9gL0SD+08OlTT32IO3/5gBURFpTFIqmFakTypbETdqzz1kMNL1Io5lkKIoVcBYwA" +
        "r328Nm2imiTuYmrmQvzDf/Dvu4/cKOgpoMfdMhRWzIWmGJcFdwUfD8GlOEwxqpmfR5PKeKrfeYMT+Vlh92Kqzl8LmjOIzj9ZcI09" +
        "gynjBAUONlDLdyEUV/Dqsj9GMSbcHmRr1IUYZwFgxT6F7qQ/deLb+ZAC/tVW151YzKG88z++sHqiN6iLi9FMbIh8fI7Sc/S5Ee1j" +
        "RIP0zPPS2yjEyKvC/GSi9teq20O2AJ/SJKeBoBM+T4gd1iakCleAvUZOsZNqZ0Qc4B0PPlFVCYfKjpLMhDrCpcELwXhFlSMR83JJ" +
        "asSXAz5+3x3RTHTcT8QkklFFl6muE0YJVxmuGe4l6SKcUpXWPDiMFjQRAXNOSpMoH/dhHMV5jIaRqjPmnsU/ff0nNhNSwL2wC625" +
        "5PaVn0mVBFwTvBg4paFgsYvcSfE8TTBgImARg3g2j0TVEsuvZfJWN1bIF3t86myAPE0G48SBSVBxE98t4M26Nim9nFN6Zowx+VmA" +
        "GWoM0YGPp9gQ5VdGRXD4q/xsQM7DzcJv97SJwvQav1z0imD7kkbneADst6o/Slq6ydxN3jQHDPCYXiehfdEfDuqg4ELiZRpOUyQB" +
        "lhgvggs8OOjbkG0JnvGG2cygduiWFL7FwrJ6Hz48eHcOV0oNJdYzaAhvKp+9jIdAF0HwgN0GHu77wbrlbU5fYvGGDnOYXkodszlg" +
        "WQIG1N+T/Yn1k7CeaiduMu8ueLYc5MVonbAb7RopH9nV9LAnX4XEgvgXf/gnZLk5cbYW9hULjyAhTIPOOalEfQbvwDCrQNXKqQGN" +
        "4+tUQAvBQziEBMRlJs5R+FHw/Uu7PLqLSfo8jO44XkTJxbtIr2FdmBV3fRsdVnJY4le69hNuUgUJPA3p9GSL0jOI93t96uDWitna" +
        "dZqfeywFYcZvR38Y1SHCZo/JryUhSE3LBNLMp538Gxq4tNnnvD9rG3gAns0wroEOwIQM6DYYijAU7n7s5SC/WIh16YaBqgguM98q" +
        "fMh0e0bJ5L9WJRB0cBycF7C80PFEOBDOAT7Zc+Oshum75OJG6KUbM3cit0S18ZykcDnh3zZ++OSTjm9m2PeYz/iitEU2UhyGTv40" +
        "JrGDAv2h1O0zBwf6tWCFcnynmgEKyfMRP7+E9tIK7QuUp+BtTFBBaFkPjEqkRnwoxp+L7DwA9hz3HCKx6nj6wZ1AdNA/L9xtVMZ9" +
        "cCnH0+zNNF7n/ngzuHzYPUQ6CUXvN5mse5n91nclNa0KMdgbKg/u5aydoKQMmPLFvfz1iAScLiBCPs8Zd2QeaVvb+Z3SNUY9LPd4" +
        "rJMQYGXj5lX78KrJP2TdHqdzdBl/ONKsZ1l72/H8QkQq/v+MLcdQ/IFjwftPyh5okgAkQnlqMv/8JXxdiyzmT7lfDeSTkHuJDM1R" +
        "DAGGACZQPOKc8BrIi1B7OrpgVajSkWLSirZP4mEzioX+uZJMrAuYCu+OmO+xD1Fa4uKbvnrH4buI31PzO2ISW2fwwQvxz/7LP/UZ" +
        "HGocN2Ha09TLSjAGOhy4O7AYoD6NM5RcYjCoc+ryYB58DFQIiASGUsQ+HBBe0GGGYhFNQDeFWH4R3X1j5tNeW2oPSerA9OK4i6/k" +
        "uC/j8YGSHwdaEOVYvwjecNT6KlNxgujACDobkBpnlVi4oB1+MPrZWApwUysbQTEBb0B1OHroU1grdV/5eoDVDGcL/3TkwzZckTx5" +
        "mC0xmtHxM/cWypl+O8Fni9aKdOmzW9MlQDF3huKVG984uxcCiCQ0PWAOCYqqBVFzSLCJgTwtL/H22jRGjhvZHUQWkUy5U1468TEY" +
        "pzib6qyj2eiKBW9RLcA6FjG7i6KlUf60TR6MXCX+Sz3sL/GocfAo/rP/3Z9tT7BpQinpYsot4vBM5oOrnkZEHDsPjm+8TAE20k8P" +
        "9Lzv00xdXkRtLnwb4pex3Qy8H2sXhFDTUuFUTNa0ugprslbCvo3jEeUIfaXT65EYtn8tW1JXd2YJ/ec+e9/L0rjSulkBwxtuIg4H" +
        "bCuOY/QJXDluPwl7hI0PkGPM9IrYRtg2RAO6R547fNkCGUHXYSFot8OnZz+5JQM47fl2FT4r2qqQGVomYuLl0cNy7iarkSO/Tfl4" +
        "ksrixIrzVtSlVwjnR5jE5CIWJ/HyZC4DyoieFM/X+Ivb0Uk4FriZWB9wyRJqWVu2e0LgQ9lno1quabl0yvJ4FWyBWItJYewjPv6P" +
        "0n8XblMINxgeSP1/ud0LJ4X4Jz/71YtlVKADXlRhX8Hnrm937WpUEkWbhnQZ6UwmW7dtTOiCIFoJPRnYIp5W8kNn1nUIifReLCbR" +
        "dKpDAuuUneCVC+zko5PiGWOLs3iEAX/9OWkN/Tx0KbpTHx8+o9AgX+lC+yWM9T7+2NK+CmmgZYUqsBnxeRemTWiQBIssh9cTt+2o" +
        "CRQ6HnNMam4tXmllEKoDjRuMJziL5H7pZtcwq/HoaV96THHthOgwBlwmPoJwrqKulo+pmQRRPVJmMYvxY3DpZZiltDN8/9zJit8w" +
        "uWd/NcH57xtIna9U1OFnK5K1a6emfUa4J5lArmU1GeK1v06oa9XK+CxynyKdJT6M8vNLMvRc5nA1GPeAL1IPisqDVxWK//0f/cqs" +
        "4J6MPlHWYr0ZjUb7TeQXInVicqFrdljjthuzljkRKRNJsfRMhMedPV5SskzVADaTl0za4vzCy0snJQuPfivvj2K6cNPc7SvdfEcq" +
        "MN5JxzT51EHjdtM8G73eWZkgnNkOVAVlVsBzbE5htgvjMVAumMj3vLzF/DoML3R6IGxCMLDW+JDRTQbizp8N7pThhueDTKxYMu41" +
        "ikZkEnaaexUioImFaRRgj+kTbUadGGok3AcrWrwVYjSY5BhfuY+Nrz/7CsbrqwISubrF17c26wF0+HTBQ6V8JWedPDn+UTQzjHIj" +
        "MsaqodJLnSKVbrHqwdC+jaGn0IqnmbRalJalCZI4CdxMtfsaoxWLf/Rf/6rd4AOPckFjjZEUShCfAinUt6p5MG1lIykOYKdRZDxP" +
        "Yh1pyiUOJ++RslQ+otMV+M5dEl+v/dVsPEm1a4U8qW4kSXyRcbWXJLB6Q4VAP0LlRZxKWdvwYttYLeZBjAwW+pGrLd8I/K41knF1" +
        "oceS6oPzG/fllNdT+HCUlkUzgeBgMSMzcFzzYgr9E+z3aAMfMvP1IhqyEJ+of8B5weYI/Zl8ijGg1GR+DP4E08bVO1IVj5XsJdil" +
        "v+hlEgWOcHiSn+3YCpOmWrT0Guj1yoD2o5ezAxyr+GMRuhPfCjwBdKk1aSiESgJ1UzeN6a3ksBq0FU9NOjQSLWgPQfHLgkIq5xAU" +
        "gwn0puzUz8zjHMXv/P1fNQ0DYY8eZwIDuBHchKY9Ve/btrM36/ycsrok/S35W7j9GTbvnFeyYYoC4tHtrLHM61LfXsGCjf2I+oWb" +
        "O8GvR4OivLEuYr9gNtA9ytHhsAtYhYnz4zT2E/UqNz6XZ9RdxWFn9Nu4P8Op5ct5FPahux/7guKb6LLz9f+vl0e2R6+PHt9I5flo" +
        "cBmjYv5wQuWAHE1eEyO4FzqOvMwx9Lj96BcnP+z8eG+jXw+CcFniocMSg7gWvSQ0NEoGjcsHcD0eGYODJjEWeTJVX79xxXR4eU6L" +
        "DZw3mDzjAWQ9819IbBLeSgeRkzHmILgmf6bfb4MFcTSR0P6wtKETs9J3mmwc9I21N6QVTr8YkrmhH9XzORF/69s/HQ/2AqRdQ+/8" +
        "vjGXVhuCjRnRwWKRZS3RBCZvMIkZlm6mYE98763vxAhQHUxuRHwbv1qL6cyNmTiJ6Jq7O2m+t6nt8XZgO/dPvXja68iAZdgPoRjD" +
        "JTtpvfWogc0u9Gf/4uWraeiebE2ieKuPlg0AZnQLolXwYDAeQun83sG3itOIPqWkAkxiMD0cM9QplUuyDbw8BaxgYanQ+KnlPobu" +
        "ZNLHsZM4ieR6sOG+TwQnAsTGPDQYWSyWdIi9QFlsuFd4v6vVf3CLMpvO5c8GPt1HT4NudiH8NCzHwb2ASGMd4/BAPpC+9WE2hJi3" +
        "7MuEXvmQ3gvKg5y67w3qwl1L7ichuhqcYJX48HaYO/cyFKc+si8ofv73/xTmuOzQ/+j3o/UxxZ5wb3sO03VqFFxews01qtLttcdK" +
        "0yibR3o8mEiKZvBqrVbf6OwSpzNbuGCcoOuQ/qztW70/JQ3QmyebbPC3rBvp9CgSBac7oDsqGUMNioOyvgHZWZoXuKtB2PDlF/TZ" +
        "yw4hS2iMcRyCfPGQkF3rJKDWYlJAacI+kUMKKwaXkl9BpUJR08eTi3N0Syhywg14x10Kh9coL+Myi3GqbKpAitmu7vvAA4gB7l4J" +
        "qfFB8NmE5Ej0Yv5nd76+KX8RZa4M41bgB+q2tm44nWsceBrxSmNL9Oxh1am1k5+8G59kkVF0bZSWeWon5Ntj2kY8JD4/RJMjNLto" +
        "GPVb7zzAQ59GD+rcUUUo/ujLf8cz+dEY2UHagC7FMfXjyb6ZJHoh0xu+W3BHABtlHPqlcQE/d6NvORHKZ+GLpY5znq2HQxzCIb6U" +
        "/XJxrr3+aErheZeyzjHfQHeQDWKYhGlL0Qg0wZOUwpMYQUr2jDIX5467Hn73nXeALyriObSCeQSfoLwW0qCKcSshPfkLaxJy1qh+" +
        "hgxQbNk2eIihHjyeeZ1qmkM78U8nO91hjqQyccyCcqg0ooNTLHuHX7q+i/XVJeUz2FbKIOxye1I8fjYJqNV1AZ6zR6pS9Aq3Iywl" +
        "pQyrLzm6FlaLpw/BPbtZSboTp4OIb/1VST2GTPEkwGDEqdG4U07CkDG+qGkL+dUQUhbP8R7Uywnde7bPo/jnf+fP6x/arrWkRBTJ" +
        "0ssnOyRl9DaN8xyohP5RzJ+gKmCdwlHywbn9YKKrkBaUTjCNcNpjdsKHU7RcmtWsH+/j7n9KLePJywtm+dV4uqA2FZSAB3QxLCSc" +
        "N9w8s2xxNXPTy7CfxdUZO4eXaxGfDJ1d0ylnySXYKS41NWdGiarjagiTtzrPoCR7GPSqDZ8EKY/RMUignyI7WdL1lISG91vTqvDq" +
        "W41LHBqsdqNqueiwCaFw4XylpyAKY0qybu9OjUBDD5uRj/58hfM8mR4pzZG+YBixVwDvEG5xecVlF9wWjiP9VtGrK7q+CNsU5QSw" +
        "F/mTSgV5BeGs6KBGxGxtKINGcHvhloJf9eZFy+1Zdx/wu+2Y1aGLSXzx8//u6OxVntkZFYkUmTjNfZSIVzGtM7cXdFr4xMJ8R7nh" +
        "k9WVZbcykzU0sYmCHE6yrETmMM55Pe+Qwl/jRCKshK1XePfFue/k8ymSER+QAYAHbD744dmnqbx856/fDC/PUfuezUqKiTQ9CAJZ" +
        "Geh5MhUvQiADSxwmLA00dSiUuA2BVug6GIGCpP4EBGgAsq2XmZx8CWLuPu+59v7mKwo1Dhv5qW/t83gtExkLUoQjxwOIpZ7MgQjO" +
        "nXA19Cd3UEEXuvCqL6AsxBeexxM/ZeFNga7wYhIohuNE+A7jCuW1LN4FHrGx5Ax1MzuZ+WIQvlZeciYYY87PWHsiDSagnvikEu1P" +
        "0W82SI/+qG1KYgUk/t7tf5d+mU9fa3GLw5uQFvgCbrUSswVOpBtK5tJtr2yxCqYSzfdgtnBBaUuh7YBlEHNbXvjMyCm5WdK/VIUs" +
        "x/pn4zRzauZCrd7XqZZsBmprQAub1nGCxZ28nvM3V9WPdfJsoukKopqbDPQlilxUOk6tx889DlwVKmshILzvhwsjpzNRFgEefGVU" +
        "BiFIHC5Fx+wsuADXG7+saPek+ixMvoTzIZwanuTiAzfxq3ga6cLh52pI9v6SYF24ysqo9VUW+wvdlmr0aIzngW9Q8ZK6M6zaUEyp" +
        "Q1p4qCWWkXOE1V0QN7xQbKKwHSUZTBd+eW2668ZOrYsDWCEqQT2Ojm7S8ZwgqgCW9nVszvSBjTOetk4GjHoW/5t//n9bT2hIQbyg" +
        "HnDbhqhkUcJ8ENfJuEfhG0UAu0o8ncWpd9dKlkGaTtqZS4KcRaIBFgm8nTRuUBCHJHaVVRqwreN2m2jBu5G6LdeNFxU54HcrCXO+" +
        "uBhMpZpNHM/CQyciB68S9yykrKFBcWIRLK5a4z21QPLJVSkv3yo9At+zMbj0Lim5lXLY8KbxsUOTUTYR6d5lEa+v5G8r/7wxN5ja" +
        "EU5kM6F6Ce4U6tZNM31HPjV+18nSmXnmjgv9/SmculEHSld6GUh3MCxxIQCfIUlYMOXEOg57LxYDVIoC4PBeWQHpPKxiW2o3ED1V" +
        "YjxFXUBpKWtxXlgc8PgYy1EeegoB91UwZ3+esSnpapq4UkkzRzyzOPNWcvlrP0lFXCchMuJ6qDei30XeAjTCr7y/tvyFZYvuiG9n" +
        "PJQcsnao0qiOFjPTB2GUlFshmihfUFVDvZVxiu6FdlsTtZDMRXFJ0Q3blicncqeoC3ikIP+fJp8IcSm7976IzD5W6cGeM2ESsc7F" +
        "RUwfwcvG/J1JxBE8vvHXr8PSiZdtdPtDG0VUJcofudTgIxoU+oJ/PnR/+f/W0Rgm38gGfbzDiMiAjy1uIw4zdPugYmhGjKzDlVDN" +
        "YO8HutJ8qV7dqPrCb3p+s5He8nNMtzchHIJpYZ6FrY0iAH1NZcGPTNkqvF6Mx7mTZF0TDT+l586vI+ocNwpQ8uw3UNU4LXwah+YK" +
        "N0+2ApYlTVHw7/sz2vRFiT/4V38yApld8I77DF8lIn/FaS38vQpH3TV8qHmdoGYiBI7haHmZwCJ1kIeTsnvj3lzZOHa5w4FpM4VU" +
        "heND9PyJrozHCH5z5t77y6to/S3jDITHY+kWljyQ3fjf/mSFpC8709VgIqEr355837n5cRwvosL54clsWi+X+u9eQ88iFcSL8Hyi" +
        "9lGUBZmj238aLMKdC1GKjQGHGDncD9gSTB6ga3xV9frJQefVwa1auRNuvkpuIggJzb70tKCziHGqjJTqBm9X0DCcc7PdwO1BLi/C" +
        "5ybsPvDKMVbBGVhcsEYePgiJuFi43IUC7R7U9pi81NhVQd6LySBIki/5oMWq46IIRR8+/oRPPiAAO7hL9E2KH9IhSlD8wb/844N0" +
        "IhdRKvwcXuXoD7w94mXBxxTaM85WoDOwEiBg9ZmWTPOVyxzXdfRylvOZm4LUCOcg6VM69fxRimcjxBQTAfzAzRr0L/HmC5sp3mQu" +
        "iRjjsMGQbvTHAL0M8Sigcnd28Erslao7R5ZV4Mlj0xkaalukevUuucF+18jcQ9fKpyiMqZ8/4X0DHULucZ1QfBo/n/g1gYtEK+iR" +
        "nAGGp5Z9MBlmXtAiSgwlWv7sTtIlX3zZ9lJKD7tco8CmpEyRfyRxlA8bZyfh+lrWf+GOH9zVO5W9RXHH+VcuzuHeRNE0XEwcBMiF" +
        "Nedo2KS9hs/WuszhHU86serQE+w1xgUu+vD4BLYnUahdYpZrjRGXv6GhEg5I/K/+5Z+Mma9SS3t5gVQR1I+4UjDzYVAAv290DDp3" +
        "bRwOHZrCv8kxqUXfShwFLo01ampkSnDfaZ36OfMBRDNxQfBIIp/gNMXJxPrS1kwpwA7D8FHvnzkJst+7rTNmcK8TvZhT36Mbw3NM" +
        "5Dll5GXiBq8k/v6Ng9fiqZP2t/56sCGlY0178HvLxcY7QfOFlgk1FV+shUtl3LhDIjfK127sIFyHZCxEvkhHiVGub+502sFXWIss" +
        "fEiTtGXIQ7OAQHhOfRNz2OPYwBdX0UdjP9Xhl3fxzcrnq4EnwSN9ruNshDQPPKBiiBroguwBu5/QJlAuREV+o9zlXuUGOANXwsuW" +
        "7Gc3G0wY0JeaWugdR0uKnIinIP7W3/qVOMpz58OVL61wexKvoLgIZeJnU9t4edQcncTzvQiEy4jk3PVnMccAk9B38gz8jklYsvNx" +
        "zG3uROtFG/t9ZDRSGShyIFvy38VJDx3KxxPurY1J2I1vBk8S5bV+PVWxgZ1SdRew8+5Ga0Gz1o6v0rs/FOXMwZ5+4xNxRVfKjc/8" +
        "Uwv5GU9TD690Esm1JsMYXcpsgrtOFDH2J/8U7GHpaS6jPIlZCAeyoNffiDHjWe6WyvJfkD+JMZfpC5wq1Y9UtPis3ZO3V4VWJ/zu" +
        "1+1tLL95hfGyxx7PL4l/Er0hkYGuIS9tKL2TlFD4ARVp0iMePjK9UM9hdqFmzG4g3+H7aegEzQeaCTidOIrF4kKMMegIlhGI/+iP" +
        "ftUOIa/kZa5+TMayl+8M9BplB9EeaBQ2wx88xJ4WGaRr1x1lTXzVs3uUAclOWTEuDhJeYqFhQ0gf0iVCfdmH6+6UcHJQthY9QPHA" +
        "9nsGSael3VcjD7Au43bKF7EKc8IOJOPnS5XnImPaFOJuJW61myg7ZHIPunGkAccj+AFODKEQM1IiFv4Ne026wevcHVmYjLyC/3Df" +
        "Xu8huk3qLzxk5BKazdTbtxRPfDd1TADPsdM0621/lmCAGHc9uY8en1GtyU74p9+2k1L/4nfULQ/o+clmlrCqaXoKKYbsyhSlOfVa" +
        "AzROPm6o34bjs3OVK2q6SGOZojlhFnjU1PXhITa3cdL1EAm8mQoAHD2oDNyJxD/4x3/2YRwuUGUf0Z0hvEExwOwj5zNHl2EY1fDX" +
        "3DqwK5z3iN+LdsRJjhUK7mF15giomXsfRD6x8qQ2rRyX4+u95EP88lm5RtRTnlhxQhiJyilkLJ/2/liOxVLHJAGgKGkh+OUWkgHr" +
        "XWBFpHC+IhlRwj63pvtMhw8QNO0cyoGp9YQ0v1DD1y6b+zznj5FbXGAssH7BiOGHvT1P8PW32aSTD4NzIqSZSFEsPlD8EdVJDpMw" +
        "+bauC6UK7GYkYzyc8fOzTQy/KmnU6nln7Vv/+7+rWbA+UrIFFHjKcbfktOTS8HIzjo+y7mN5xP0PuK3ZFRgvRPOKiwu5QPJnGIBn" +
        "R88xfEYfTbDPKTqLRGFuwuGTW+1ZAp0Eit9b/RsZIV9Qh3Bt1aKm7prFW3+5deYT/c1GtMfw8xb0DH5IAh/xS8Y+QdMiKFRFmPcg" +
        "raALIw3+UKk3Aeq5axMIrdiBX3Zae2oJYoFwwQro4yGEAZOJ7COXBCkVLjT5E4hn+kvvogPPA640tB3QLpTOq86/+BgVLrR/1jIp" +
        "qZ3JaSmmyDfod3fhXMPY4wzkZkdNB3rre8LFV0nFofpgFkqLL8llzqjAGaUeb6x/OzFVFe0GvQRfD+p0FpuaxxXBSpZPlrYepvrq" +
        "TqAKDyeadKJwwW2gcNxk0mfgFzw4nR9crMJn0vsWVjFFM9kQZERtEcDQ+eiTLS9SjCquYtKReLGmD+Edi00XwMEsRXXyw9aLf/Dz" +
        "P74r074I0Vf+sPKLnq6eYWjp4RzFn01V++m7WBQ4/eifThhdi1XMBfoPq7C0OCn9sAwz5Wyg7z5EeU9TzXKv3cS/WIaBzNzrVgQH" +
        "OVK74dOnoADpksKI3egVC33G7Egvez9/DnaC4Q9wckWdQdtwOsNl7rtepBDsWmvi9w7Lo48GziPwMSYbTj7K90gksT5i2ENAbE34" +
        "Gpk0fUfWRfDtqIcd1gOqQNNE8CVcZI5fqPmkcw7e08OT7HeMDhKikcB9I6XExYBhS/w9aYF0AcJgOvdkuP5RLiyYM54qsUw9IjwY" +
        "TUuSOXb3jjoOA8qOziboCaa/h2ABtuiPfvN9O91S9I3qHfMeIo1zCo89Q+3Ev3r9f7nIMZ6rMeDJ+QowboSuYCPAnsO14JXyGy17" +
        "IEXiKkEsPRwIBmqX4XIP1JHrxeZBtynfvR4HjVnmzIgfjJ9oooFGydMJnBxvmhDlNPtfkFrqYQJQBJsE6kXjuGT8ypKa0UcX2pom" +
        "Dm6nflNR1PrZwg+FPJ2EtTDcyBKZK47uDccYWnRbGnv0GvgCuhCyTM1W6I6wOTuWlBYSHExnki5DFrHZEb5I+wjchVnvqw01FiHF" +
        "2QW6O85uvQi4K2gUQANQjvMjXniPb/1A9Osf1SV7CHi6h2IMy2Dt3jstT0TbT+NQ+cnBJQ7sCBHj67mqd+GIdKLw8n1VBHp3lScD" +
        "/QZtKYQSwuxs89SBczJdRem2Dw/jbpqwgJDBQGGp2S9xM9eXJ/Ybk59NI+jNKxyX6DbUEyVbLkaqVyGqYQhUzMLkbhwJQLixp/2L" +
        "nGawHV18L65jVTu/3bpZKic3ZBMOyHMW1dSNyp37YAdIJrJTJJ5x+ms/ZqBk6GPkH/1QqPgCpzjsM/XDOpm+9+mz3yeUS3ZL9erc" +
        "fxLxdIk2pyjj88S9aHs1iuokuijYTNwuKcmD0xwFOL7HuiJIoJM4XslbYZhoqFQpuZ9iPIZ5Fd4ntMGgjtQHHJMwrciWWJ4c/A/h" +
        "hyjBt0QJ9y+EN+AN1/cyGJd97Ii0ey3CK9UYXD45OYQpie6vxs5xGfijtM03+q1OiwBdgIIkX8kuwAetVrlUAcR/9cW/sZlygtzW" +
        "wETHSxnlYm55rMBO0EpyI0nH2YRuhWk78QBSb4JDWBnvH1E4jJm1ZfFJTDTvNf74o1KVaD667t5iQtkWbRforVzdoSCMD+gZXAzK" +
        "0cuzG4+spbiKdZv7fQ/6g9ceTUKRpijHdfDewzpqjy/xhyDOU7zzMCQYpwhX1GdiQSwS3Du61bAPPKQeE7BM/sCzGOM1uRMlBn/a" +
        "+Bb8YqJsB9ddKC95ENT+wJFmykSeu+5rixbiVpxK/5iOM5b0yuKrEGvRRzIRXEaQR1Ap0QWcHMI9CVcjnX29H8vezhyNpTzI8Hwe" +
        "r2o4HodyO4IgnKooj6Y3UfgSHKAesSlYGgw1V+AZiToW/+m3f7w6tFuWchFdT8V4TY1i+wxTwxhTTRASigJMbHA7q5/tOVdtTlPL" +
        "ZsTFJMQrri2dPuEabHHvnr/ThwusjT0NNi9UqlWb8y8ulbxkzPmkw2bwXx5FJ+Hp2Q2Nn12rIbPZRicHuZs5caeXUoQ5eAULCXsU" +
        "d9DHo3vu4rYWMqZ4SZEGjmAR3OFayBzaWsgCZcfQki2wt0wam1t3k1N9JhnD5pMrGyrfylPkTQLXGcVb/v5RyMZ/6QZTyis2fSWP" +
        "scxy/+mI2ojZ12NRhnNhxNKou2HMZWh1/Qzi1+P2xcc7n3S806JvgpO0UJx5Pn/XjB8amMVYal9IfRFPFcWMref5i1cP3MfEERY5" +
        "bNau9aHeGXUOk1KLX/4n/7124UY6r+XE+UzwdyXwMtytoRsoW4b93IuF2O45eXQzsmUXdlphLpKEb+NhX8nns8zmrBN8ecG1Decd" +
        "P/ZmOU3CAolgfYV5DtDQ+QXNE3UxZ0z+EY5xmFzJhEUtjLm2E6EzQYNmFiT2MBIsJNAMICP/Ef3W9ala5pTNfa2QkSYYkpY/SUUt" +
        "jGeuN+HW8qEicJT2omTaIahAx4NvPFyUMjnAuYNEkTEYnfFckFyK19Lozh+Osjh5ZEknepqF+Z29AYDMQtmL3BjgX3dC7uOdxe3e" +
        "XI0gEqGP5vlsUxcu1pFbx5935rEb7kDOe2RBSSajUh4QwsGaweMQ1gHyAbUA1cPhwEe022y8vsomsRL/+J/8efxWR4U0nqYTb1+C" +
        "r0R+JQZDEENh0Cf8eXCbAdd3OgcIZxcb0AKTq+AMGSfoEoSmZosNQRzCxzZcpJFjLEYRvQJliJ9EX8Op5uWEhqUfYwwDTqQ0OTRV" +
        "yIdYBWHT4I4IeyRDvcfXE46XPo79PhfNQqlUpgA5BGX4hHJ2aV5GmTxgv+M9IneQlBQjmBGhIK952tPYkzFcmZAVNI1omLLLEQaW" +
        "QDJBOUB2DT4R3QNmFCQGM2IMcKERbg2lNnaIg961unvJX9j2WThsnE7UZBWduuA7n2WSV9H6NG437pwo+00hpslsoZJERhk1zIez" +
        "S2dKLJR+q5ZramLBHdQPdnw2/tFOpjFkYjgE8Y//t3+WMo97EhH4o99u+Qt2nlTrKK15rKGo6K+cXWuxnorPWkcj6d6vIhc5GLRM" +
        "J74Z5c7j7uzTmsedTTLZJEIbXkqZDvhkOY+pqTlFumN2MT8X5iT8q1Eb4HHt4ivX9wG2ovNBjJi0+OouJNcOA0iDOvLNIgxLPMZa" +
        "nzjZuqvL8WFQx60wmtqPdjmEKqH4HMLBxwjTmMIaqiyEgK33iSedInQ83+JZ8wH9oiVzhMucY+b9Qco1RZptT8vUjjOxQBdtIz9E" +
        "gmBXR/U2rioWT2oUvL8YpqmKe3WYkbqKdAPSQhtLcjCR6ADDShRa1G2YN35LEFZyUUqIsIxht+ekDk8BDqMdmn6VxBdRshntYqLE" +
        "v/jnv/qISAPmTdiRZIFv1KgFbmLZnPgigb5HH8Fww8uNcDlUb8Rsxher0UxJydA7sXNCGni64DGXeq5uU9znZAQuEYdTyAExw8fc" +
        "xR6jZyh/CvVOuCsO8zCtVYZiX4caXPY6jDNDC37ztVtf91aynxk/tdiofqu7e1FXaBLx5VWr+/BXm1QS1DtnV3IpYRR4mmJu4C7x" +
        "akb9SRJwNfM8DQe0i2dlYoiJ9B7V1/5hNVzGSo84PMJkFYq5G5cAX/ssDioNJyXDntaDDaP6SFS1YbTsBS/a2M3deTXksZhMcTtx" +
        "1RQLQNViV8rQhxJQjdj1oR59WbmkClEuOUYhoD1BPwDUtvncDcH5TL/OYtu6JFc4JfH3/tGvzA5dSQiYELSXaoVO1+7zUWYLSgCq" +
        "gCXQ2WKS0ityfGPxrUkpDKy2baRfuJN0aDl+YbWieEmtpGbjXYJUBeVhkVB/RJ3hp9moSXmHt0TzWB0ydwDOW9kBX11jlAebG7ka" +
        "r1DkluuYyxGfRl07eeypGWjJ/MVXzTmRh9/EroXWARY0y+mc0FrBdoWdwq+dgzqcJnIRwShw3/oKnMhoMsim57sJTI3YlP4UebdX" +
        "b9d+PRnt1EtgGaCeIlaibVQFIv5o1U/BHMSDhIRw04yvfTIvcFwau7CpEGZizaV9iDElpSTZC1FMxbPiYsDjjPJELCb/yy+iHmnX" +
        "eVe7Y+XYhwlJOdXziA6Bv2UOb1i8+Vf/DgaYevQJlhHrVTgXmg6gYjG94CcUpcaTx4xJZ7z2PjuirERn1PCkDr0gCTTCexcuNCUt" +
        "mg3zANUM8owOCdwAmQbKFcuEmojDOwvvWAip76G5x/WXznzbZssQJ8EIb3uBVtpB3nxMorM62gitOMtgFJS/qNLfbeKn6OFT2i9o" +
        "O/KiYgUoRw4BUge7B0uARSSLzkwT3qA8/hialjUINQOYcLGC5YUbNDojdsk4e2tXREnNUctn0FPrTSfPQfcImxfMEpQ59o8h7GG/" +
        "d2Wh7r6BbKTnD1JuI9+JyS56aPxoQnlFYsqcUK7gfBXENyyW6F/DNCJ7wujE3x2MPTmr0EToJ2pJShI1Z1NYeHcRbrQVv/zln9AK" +
        "8imxhou1gYyfI9KlyCdsKsQcxhnXRKsJJ2vjUmAm3UO7V3VFqQ2YYNfDhriwyADlGjFHmYqT8FXlok5ca7/Owr0kquWlpEfrPh29" +
        "/wy/OwnfXPp7qwyAG0h0shZWE1kRihlLg5kBV/qDoy/u6mI6/Pqkvj9Gt4O0He5jgBuyx5AdfejZn33twnUkZ3GAGUYn/vEJes8p" +
        "kSZxKcQQh5xJ1RQMNgIWsainZjRq3kOhTDNo2WJ9UKLCtsfdir/Kwo9n/bgbo5O9uYovf6E1hr96Cq7B0ssshabmSSXddbhCMRLj" +
        "zNucF4Q1Qe9AMR0DhiAPjyZ0gV7pqrdzUOk3ejPj9Ax+rW6vxNu8GzoS/+LijxegHNHlasxya6NwnttGM9TaeboSbqOJLtxsbtyz" +
        "UhZBQQOyl2gSlB7sJtQ1SwXJzyjPSAQcBJxfvH32bQ7rK5WlqH+03ZYbTeVP8PTBNX1IJvQfJ6Pb0G6bhkZJCSINQQNKllGYp76a" +
        "G6HZbeNkbpYGfviLWbeL5btuvLL7RmYDSQ8/SLu2QgCqOYkLPXnNEoJ2+OGIvLVcSFf7xGFSkX3P0RMkR8w8VAMl9zLf637K06W1" +
        "L6ptZRn5NieXsc4guXWfWHz87FnQ1ZfZOxWgh4e9tJmna55fMqX8XtpppdJHcXji1fdSPYqJpecR5b1UKfQjmw31lW9jvLiKOw7H" +
        "23DzVuGRnxqrY3ltMJ2iOvP5RxD/9D/97+cyvL7seO75pHRP5z46xs4nLCKceh9byi+H31phB7Xogc8EBtyIBnEDiBZVQbcr6rqw" +
        "+xSuLT/1sPVMjVtbeVnig6S6p9vBd5ncaBh9SCJ5fa2IKb53XolizsmFw9Tb1DQDvYogmfZW+R97fRUFpcOPL2nr8CbBSZD37KrS" +
        "yCV0FdgAcqZSSYXnOIYshKNWfAzf7ULB4DZ9QLrK5N6HoDEmfJcbznE7CCP5oqIrw0HQGfUysajAsjBAU+Uf7/X9hg+Zu7uK5nOo" +
        "BOWPXmREczHKkHmx+8jjGHY00mfOHZUTMVmgDugEugWne/ppa7llv3OXA7JhJQmABNFHNuQJR/56htksnDJ9SpUsfuknly2dEB4S" +
        "t3Da83w69Ii9dOl1/9v32fVvMflrWayl+DpUcw+16O9FfGQYQ5qjucB5z+2Ot7uQpeQeTTywj3wQdHclRAVFFLbf0vwlmhreImTv" +
        "VKN91mNVyNXfHafaMQJY96TD+6MiHYpeTtv8eyscQGup/W2WzDy8GTuHuFPFU45X/XNnBksriGwLIBBvINEcesIG/sM+8OAqJpXq" +
        "N9fR2cMsQfNKoGQ14P6EPEI2RbrkIvi2Fs1I87OQHDTxMIPH36Dv/fJSiC8CZHZ4UucBaSaWja9/FEqLx7MLbUgE2ALsLzFBRWco" +
        "WtcuhGxgPOK+tuLs+ggHBXwOU4OHDZdTOV2L+xQa5S5fR2nq0YPxME5Q/Ff/5R/PjvxDn915g4Fi5U8n9ekp+saLQ0/fn0H0OgR6" +
        "VfnMCzngnqiLYYggTTDN0AoYU+wc0kKYJR565LNhhqtFcuFDY6C00I+oV9wRBQWRQBNzcRmmyIUNMCKnLD09/zobWpkVFBiTTXI4" +
        "K9TQjrIo/TzisZONpc0BphH5gPddrxWhFJrxVQ4LDueBWk/3x3BorU6lQrqYqmUG52tx987VCRiLqhN1A0hwXYTRo+wwGGg15LfW" +
        "xyAC7R7Ek4B1QhnQSuAD8NNLmDY0Qaw6dOdw7sLAXkSCA76apsUt1nPTA94G6M909nAwrt6NBJCjOJWQokragJpeL8UJ0Bq0F/Bu" +
        "JqYqgPaPXi5VkO1GYePzGZ9OYmZcq1S101cLGA4iH0T4aqzemvRGBINzNewS5SvstpA5DIjmzDGhjaBPYJLC94Mb5nA1zacZIkB9" +
        "DmJk0YerArGCF2YOCDVfdwoCdCo0jYorr19w60Rl8E0uu0HKK/foaOpRpX531TX7hDdxOOLJOpXyoghHDiB8M3o6iHShhpE+/cYn" +
        "xyGbyyElcyXGGrJYXN5xCPizbDzGdPiIrgMGwCvUCTQPmBtf/MwcU5oNQjPUczjkWAe+LfCQ8eWeuzO5H8Lklu0vw+N7Knuka0GE" +
        "OsbBhrIUE4L4SZ01H6zfnyX1IS7FQTjxtXQDF5/pssTzz2B+iEtA4VwQMAuySCDM3P6zLJ6kiFBpFP/8X/yZ0mDuqZdyNti+wQ2Q" +
        "fjRwDGvmPFfJ1317OSQJyKM0L/TegZ2wSpAtpil2fcgHjnNsGXYmREtEhPgI3oMEHBxcCI8j9ANpxO6KzwLWFq6n9pxB38jSsiO4" +
        "/wyZDWxhbkFW1Hq6Kn3UyMOn+IxcHaH+tXltZP4FBovPDzDWPCR2fksqopfPwK2YF4hnN1SsJrJawi/fURwFJl5E9n2tf2zCVUqL" +
        "MiSBXzz1TG8nZm2Gly7pWNxkfafowGKf8l2L48R/BPA/YZjBlZD9CzYpTAhyAccZjhjSNRZ3fkce9rJ9DJESPdDqeXRbRxdxOw8v" +
        "tl+uot+dqt3E84wLjXrlqymck3AxCn8U7TQUBUCKoEH8Z//tH9dO0TSUt26bR7Dlhyaw5ZsMl1c+9aQ+RfaozyR3BxV+CMkzhCCa" +
        "o4/3IWx8U4Vr62Hjxj2HTAw21IMvUSlCwSBKyiIIgNmE5QUOkqsLc/PlmGr+2AoQlBEcj3RWYp4ip6QtyyFMCyAHH+9J7IQaxEcY" +
        "6Rp+N1fRFrcWn9MxLwRFmKLct94t/OQr9hQPoyimglnMbiCfhaGW2cDiBZ4PylwAeY4fSe7DZsOl4Vvhjh9IfefWEyd62HxKzw/U" +
        "djQ1sN7A/ysyUUYro5hgSCGcmR0mH8b6t4M60zshR0/P7GvhYykVYDOGytOr1s06+Fj5UcGXiwQZxE6OQUx7nkn3vhVwEkHzMues" +
        "8GeFAFgmTvzjP/z3Sezzqa83crunfSJPJmRa/iI3KKAepCMsGlQS32d+b/GSMBA+DEbVIe1xOpdVQHG0uQ/a4BFAaSRBhUSVY0TA" +
        "DIlibUIc+yGn6dJgac+97lNvpjZSdE5kyAkB/Qj1zl15xxWcHqGvOVGYSGpjmEypj3F4xPkLQy52aGQv3Uh5BssFmcz/5GzsdXzi" +
        "q5kvL6HxZCMYRyGPLEpxdFixv5jROVASwer3XVeLwaviMsyOpvosu16cHc4tygv0RyicOPzCthimG2HP3M/AM973fm7h7Z0WSOER" +
        "zUi1dIDAI4gpmoLmV1HTMTY0+bmc3zkbQbO0MeL6ppMq/E3MJnGvG7WY2m2rSgvhwtheSChZV/T0P4Nrucy5LUB8I8V1cHuQI4uY" +
        "cQCUXLbQNh6naPKo+jwiAP88Eiz8KfQxy0kadnYGHFn0L6GYcJyA77FnyBTXFb5JjIJw6CV8Sp5txg1eTsLT3D15GA6QeugVnoGL" +
        "lYgFcwGhEMqSDJABRzHKFjuD7jXjHosPlC7UGPPVVFDpzwcO30mchOM3Jv4K3z4H/yzcTfhsSVkqUrH0Ri315JI3u1BourntO8QP" +
        "X6rfs+Ngxfac9hvK9z69lCqGufU/vBGrDYT39Jezjq/oaqM08Y/ZmLyTPo0mu/Cbmvlg45ewXqjTXShvqXJeKRiseIhoFeBOhe9G" +
        "nCIslmP/1dH34vFh+joOH78+HByudqnXhhzYx7QJIP72P//V9gHHmtc5EmAuYDIlPXF9jNOG21o5RfURxgcYDIlr3LW+3/u30xgQ" +
        "YWTOMWR4HHiaE428OVmMxYpFW4XJs10u+ShEpnhdDOB5NyZ2gxgz3VrpoN+osSO38iqHrgEkzCZiMrqpcY8HxT2kUzAKuwEsAI6g" +
        "DrztfcRkdiYhsYBo/BHfHzro6JdDvpyEYRn6lNbgXKOeJDSRX2ZifPDrhzCksl65S8l5DeCIXvVdxnUVJQ6+91F+tNfWHWYSjzw+" +
        "MsQYNbgdoK9c3tMcaZOGeSo4w0PL3YO3nZ1P9ORW5wtsKEwzcG+7MUDGEu9sjhxnJn1z0qnV2u5S20+HSWHIySE1EDEZue3UKCCJ" +
        "g/id/+bfbBO3nut0CXAbJgtXroYxZs5Cm1B7Vu57d24gzyidipDAh2jQl0psWGxDLinu2HYcv4V6Ah9f/FLJ9JUqLqB5DXyDN59d" +
        "62k+C0VskPC9TJLEX+buJOQ54Jj5NGXwWNVg05CkKBBICfjszke+XWOEcK7hMLLweOwtPoc8FsfWIOGlVc3JKyAzB3gL0wzf7mR9" +
        "UPGAMEqu8XiPSUpNC7tKvEJ7M4QniLjFdeQzCMOnZPdDkkY8SnwOxIVYPlv3HLYjTQMgo3Xwch77wX+bRbmCI5H1QJ+4GvmUeh3w" +
        "95glcRMUESyFwJF+0P3C6LvA6atzdNFEzD62LUEYIlK+Qw4EELBq4qaK+q28ctQKFH/3v/2342rEku8E6sLR3JDFZh/HLW6C+tFz" +
        "3+CXMXBKM+W3TK3k7aFjJa7nUUpYEeiG1zv+buejqbh5o6OSHYE84o6pYvl1PZS5EzqYTlZBAWK2hZ5l5yljNEnYBQ+CZ0GamBXB" +
        "4QBtRV/MfBmHD41qtz5lhDk9RsPkUklPVcJpro61ubJUlKLXpFFEV2w1JQwyZ0Z83mJEWHj6afRXKc1mRG3wTNlrP3du76LPtVIN" +
        "v2p8R3Lbc9ujO/PChyNi2ZgygX2MD9bNL+N5JsORm513TWgw5EH0zsdXkVZ08TLGMTYoi8BoVRMCFPyG+etT37IeONaPRX/OuiDS" +
        "OumNFk1Uj+owgu1EUGGuiAAlDiIaonMUdnvxtoIxUpsXYQdOJC7W8MPa0d9GHiiA7x8w2+AoPCRw9VYehTNbwU8grsTQcGJxmpA6" +
        "hVjDbgpHgulA4RfWeo4eve78uYzUCI+NmG7sZDMehJquoNuq6cQ/FSM90aSip0NfKh3fCqXC/jOXZOtbNSGAClaX6pQbeVZC4MOm" +
        "vjBa5CKvQ9kDGFl2WEm+Yggdbc/AGZDG5sBXuXBrOJ9wmtEvpt1vfdwRMfJ4be0cheN6E5pD0AbsQgn2oFTvxGrT23u7vtHSiSYA" +
        "RlgPLA9uoaWdAydi2qOdiP6N1o8wtVz2bEKIQjRFDvNx95BdfA92Ep8ikQ9IC78bMGrxIEBW4NRoIryaS5q7SHnx+//63wgFhzMA" +
        "isseTYf3Fi6cLxYhagES2S3sxcy9jHI7Knv0tof5l1GkUQk+zR3MABs4PrhZD8WK9K03EsWRNp5/59KWl8O9lqLTF91gjuJjE2+3" +
        "Num53Zj0MM7OvmeRevnpaCrn+NnnhN8uVTSHypLXQjGEPQeGQCgrcWhcHVk48RDDYh7PhWgCu7OfWoyYtEYuoa4wAcwSbBHgApBw" +
        "6ZhvHc1CtgMxEKTIHnaefOlFK55bPCchicTU04zCtuL67E6bYdY72YM4hbTHY+/Oxz7PdH6h40D0Ovhb/ia1YhIO12gybI84f2Kz" +
        "5GIeso5OXZSMPDwgbgMgDe/5eQPpC9gDV57lEdepfrvySWY7J8Tf/aM/P+zZep4IGQepR3gsxWoRig4IgnQUleiIf3sg6XCb+2ks" +
        "b4I0WVAR7Dfh+L2tPrkU6VUh7oTlnsZF+DANUxB3Lri/0sao6G7sULe/EacXt1eQ9YEdi1hNBlNGuGmIUjrcWboUy1U8B4jqEFI8" +
        "KRxH1FMCB+PON5XlZ69jebo0eqS0EnLvRON5oWeliDSXAgzDs4YJgfY4eoiPqNYuvzLhSaPBQYr50Q+NOP0o1J4EiZrQEDYyeIZS" +
        "kW/5p+debvssUTqWjgECREfT1sZm8pt5Ajkmt2GSo534KAlyr7Clx4HOGZeAv6NN+25oS7cnqTs1RkIV0FV46CAirASvY+EuMf/d" +
        "cPVNT8D4nBxrKX7/n/5pSEJ2EWrp0qC0xcXcuq/MC+roHlaj0SSfdlElOCAXSphbJ4+0OovtGT/ZvgFbYnS9iC4WAMTQweoU9hm2" +
        "127cRKX1t5GRAX/Tpk+9yM+OJuohwmJnxOhXWYAhWKUKEHsOVkMZSa0pUUzMQ4zyBnqkT1vn9oZNSFJxkyUbbQ+TXrBAJ6ZHd9G5" +
        "LCdRkC/ZHIg1uhhoB0CYL8LbyL54ddKwMNyjkAq6s9g4vEEPIPqOmj4ctkPcUdFiU4Vn4YKii/2oXIBIDqnsCxFZvFwnkzkpxFxw" +
        "W4kCyCLqib+vZNSSzyC9Ml9kjTLi05gcDEUlqxgGL7ZAkFIncXGD2R2kghZZEMzfNypvpAkg/vA//5X06FvBhU/XLr+yWRJ8pX6I" +
        "ML7ly8KxhNMKdwOjwlzi6ZkxgZSpG3Agt23bK5ndXEo5MPWgl8AF5yjuA7qUL+dBtBA+030rHjkUgrKefzJjROLVTGli7X1joDu4" +
        "PETqFSaXDiK+Kmy/DvUosaIPtXvpx4JlPot4LtO970eyMdgr32sQcfRO48rbrAhnRbuECgubM5cJLSYcACdVaGodKYCbUU9cO8Od" +
        "INIin0B/grrmw35wAnQpsWc/8qntvQvTVX5Vimyt+E3c3IhmJb8R1HlMPGMMQcEFBRehPYhtBMUUVqtx+uU+TOwI+kdGGPE6ClsN" +
        "xokGUHtcfGPSr4eXgFEvVpbbSu8DJDEIJvG7/+DPYMALkFfzYCejdhQUNwRSwORiGAuQg/zxB2kUpxM8DaxznsygMogB3p8qg/5v" +
        "XZaphEbhReJL66jgM4t9K6bTgATPTURPwbxYlwm8Ul3grfBXeXxJXMXaNr492OtLWayjVUZ9CdF8pIj3PyTHhnDA4+B32iQk3zhp" +
        "J9itydZBj5KQTkk/T5Nipq60ERCqNoIMPgyhIJqmCE+sJRtE3fN1Oeal2cQCPL6PmBjDXj7v/PnDOTqYdZ51KbQJ18dh5HC3LF5d" +
        "RWIultrVUh064BUjUd7z4sKS4qMWkwNwR0OOAqgsze20STC8jJk5pDVCuGtywLOjIQp1HBZL96VyjRHb1diUVv82pgeIFA0Z2xOJ" +
        "f/TPfhVWIcv52lEYpc28HsXJyixxulLuqL970JGDyzv81IcyZpz405NwHZyezdD7i99JljcoApZr0+SYD6C24YefdNFAbkV/FjXD" +
        "C2NkYO7A11AjyLXqL2necjyG0yrNX6l14ccei5N3z3LViY9GHwaEnv0JzvUYGGZfRemaUk8P+7GqTWFIb8GhWN3hYs0+iEnr94k6" +
        "PcHg+OpSvIwQMdw452MUKU5qmzzSYPWZ1dHCSfuhocf72ghYTtLXmk6AQ8wPeX9blvE0WuydcsFXbJ98MhXRnE7XrpxydKSIwz4X" +
        "hmHdeCX5nOJXNMYjjI/pfleowgkJtlW7WmZW1iOWEsuLsWNKTxo3+q8rVwl4i0J3YI7w7IL4B3//z+OduC58SINrVdmKfRBTAitg" +
        "04n+k7I93C7h+BN6CUUiKsN1advG17W5W2WrKEoZVcnJXoxMfY7PDyowFsD6FALh8czecbxQDWPiOL4QQVKmof0Cy5hShOwynJXq" +
        "rQxNiE1YGF/34glCVwc7+u40vo6Sqdbt3ptz+GiGLFWjYK3EuzL+8s49AxIKdyT3kccrMb0lo8N5hLsb75cQTtg84SUYl5Aeua51" +
        "EuHDtP/x3LXs7ny+vI2VBBPQRbApzCSNccQkp4UPLcgsh8xwCihSfDB0Wdh8NX6qI6FBpRDVqBSoOLidPv2UXI5ukrv/0Eb8fZwI" +
        "tA7HAVcRqJdIdrRF+vwT5VslLoEIjSXfgIxB1n9Qz0YpHyP/SYoSGCkVaA37DYclbCO+yfDzC4gGviS5F6y8ct49DfX162yay2Yb" +
        "3jzR55OXLV8m/pgo/wY55vcP+MszDwM3xk8cDttREGQzmQ++usVIQjLiY0xvpB/3aBqMb9zpTl0efVdB9MQiZzOBmrxeRVlQ49bX" +
        "BNHOrBTRhernbrLARDp4H0mLLzbYUd3+njWG7T1iDjjjnMLTOXIOkjmrwec4fJ4UIQnqQOZ70epWrtU5Cb9f83fA6uTpx+7VQtk7" +
        "jlNMLvi+UfMjd0dYq0EltL9PswUkxPu/TIYIywDjQEVkr70P5H6apMncZNh3T3E/oCPz5U9qq/j6EqTw1czNRmpP6L8Z8wipk/XS" +
        "5yUIFa5LJ/6Tf/FnjwMHp3zNq4qrFtMq/HAOKyDVInoMAnaWlxMhFIyPXNe+PthZpqdfy/v7Ibz3+MmnbSgWEtqwRhczfnwEUKQU" +
        "ccf7FDGiNsI7ojRHHfiiheY6VAnLlH1DNNLy7QiezoP89OSj7wdbGdXhMLCeSBdDdwpxzW7w/YwWi2gdyeiS4zR02h8z9h0i4zeX" +
        "RunwEymxZNjTOvVjis8dhR7LNswznxxG2MsmyL84cj84n4COhb/ApFPZCV5u6VDCstcrI9evQGp2MVQTcJa+SIaHH6V4CbMMHg9R" +
        "WrGY4ynDVe6K1KcnP35UOMoYuXqOti/q8MlqRwFhhVQqGPci3ZFIwzHnpcLn2IaWLphaHS4Ye0fib//evz88hwundlm4dLA5c7z1" +
        "ya1wK3KEWqA5AJeYExqGYwZdcK11X8pi/8E8NW2+1DjTFyh1FyYlT+fh7OQ2FtixNpCl9BLz0LioYS1ptRv1BFWE8YOwSL1HO+P5" +
        "0qln2uz08/dDU/l0EklBAOgBkxraIeCFcBFOV3L+DfJdKNdOCug7kTdyTPx54dIpzxHMRrpGOIBhAteG660yRXguw53GYvDeoXb+" +
        "/l69DKwzYVYsFCVSHDMupcIzVxmPa7pB8fPOHCKJA+5OiBrUex4DlmtMu7AneX1pacrmykdTX7NyR6makB7M4Um5TWiP7jQV2sJ0" +
        "wDKBKEAfgbrwk0c6O+FOgvaintjZWUIjcoc8oPj23b/NtbiYS5eF3YDnI3+z5lkEpwLcnD/vfHmAiaBQMAFWA++NjS6kjkSfeD+H" +
        "eZ74OciVKKcik8D3tt2E485JgPlMNHvnjv5jZqYDXcQyXcFt1D+NsWIOXkCH18iHSDw36vzBbhpzBZSMIUTUWc41nVO8yVWTAGj6" +
        "5auAawPzsWfseqo85JUsRmoRMh244MejvvAsSwgxjJ4IEI5oEayidINpYzeP0B2dSCVJxQy1dtQR15BLNRQsJqSmIF5D2cG8C58j" +
        "mWp4ZsgTcRF5BOwc5cFPc089jp2mgQZBhyDGDbYbl566GkgEkAYOEm4Frif+ZMWdsE2BGykKQ48y6D3BiQJSFshvIX5hGebidinI" +
        "sXsRbe4v/pkN2sP7+ELwX0LYXo2LSXJ1hjHAY88hDsmFmC7Zqj7sWAnA3EJLmwe+eqRdRALjRFg5jSY20Aez15QOPNOSfqZzB5GA" +
        "Y6/DAOR9XAe7VvAI0W/4vXZDxPHrpEuk2thF49sLnWukwNEpTDxO/o6PwfMH3UA05CxzZwr7wuFn+0h1apLxoML+0r52OHwnFfPO" +
        "8rUJdULTM3kFm1z5o5aZL1b6sPfqZZgh462qLkBMceQwCdLNDV/YEKj+ewZ7jMAfa4EOnMRdK5MXdh3N0KrKnw8y7UZOxNiFz62/" +
        "7cNiKro0Xvlwvk2DgKlHlbLb+TTywvnoR+CFMINj9mEuJwuSEXchcAzRToj/4+//aXxJ9zVMka6v/OVy+MGJrlOLDZ0YjlNnBC6k" +
        "HLfQW1ihkGv2EqsTjNoVM+xDaPtQ65BL+cryrpSZCX6EeqEkQXeyzvLSi9VCnTReHh1relRKV6zGENfu4wn4cXBbo5VcFHLQeJ6J" +
        "3OPM8yipnqPJ4A9yFjFtToqPMgXIp8EFcT7jMLXNSMVRLBOOEMSlfQ+8PeBth90CtaLQQEswQ1QBeCmyjF5q+mC9H80EhRxJ9RAN" +
        "cqLl9DJMS973Yp74CAH3sQE4zMescKqwI5HNhS5hjEX3UzjtAGwIG9OMjBOdXkX56Enh8hrLIvywFqPHvMUogjjhjy9UNFYybgNO" +
        "Y5mkpDqwDWYC7ytnPzvxL7/+t+qDPQp1V4aZCt0+6vY6XNkjcfkoN3VQCgWK1kE+xygCfZTnFo3jgpRpyUM4G5OiKl4JpYQJ9ByL" +
        "8hiy0f+0H/3g80iuNNyNdryUeMdFG/Y39FhQfMSh4q4ylXMrojyVWgrPqDSaS8oT1BIjid07nqfcPcoAGM9DkXnR0IcO/EEko3Jr" +
        "E301znSIdRgP+sOZ+Z1LM0lPuDn4UIcZAkvEKuiNPT/a42E8Vq2SMjWcaXKzeLIQhaKrI5i9TJauLNzJUm3FmwqHhKvI0ykejypq" +
        "KHR0amiXyaKkIVHxWsplhJlwhtVErlYMDuDRbT7yWdBSwzrztRK7TJY3ot6HRFI0I6fh3PH0BIf33fGHulRK/OEv/2xu/EqzUCI/" +
        "hNYIUQKfxFHhCzi8h7yXsqfZHOLLsEuDM+gaJI2jCaGC8RkE0fodM/BwVsuCN698NZHnj9627m4Z8zLKMlwpNx+djWgQSp7gP4yM" +
        "JJsx1L3LIlkWOhrcSvpxHVEBbCCNoHKYH8JlxM+RcIAF8yozz4noT7IfwM3C7YyTBMrYmLvmsdP7H6KrSPjcf3ew7U9+aah8LcUS" +
        "zxUrAzXQ2NjOBlfoOI4olW8iKq9luBNjHhjwlTSXufuh1eNfx4khHSB+jByoD9L4FzkdhEgCMGUL3s0FBswknCVt9nbOuESnTGhJ" +
        "fjpAsrfLhc5fYancmSVcwGmEvRbfTELI0WXwQ+ybjelfRjWJ7lDJ7OeiHrPZ48DPtnVc1N3ynfibN4lq+anrpzc6npAKPBkAnhFK" +
        "eIzspJAR4ckGE/zkWmaXVCD/SOPtF0GdYzjDX3VVWdBdkmrj9XEsriREmCRu94MSgz/WQSKfwEYWxE2SZtK1QZYqroe3+/47GcuM" +
        "jYCGYb6EiQ2PW6klyLXlHrM9vdyaUoAqjK9VFGBpaf/d7Lc4Ln5ZL3bZ+Gt91o38pV5NCAPva6q+4FBj9IR1nuaIKBkI4lQkkV1F" +
        "1iOahN3EA/rTX2hgubvixSdAA4798m94d6HHb8O0HGoV7rf66/fU2hA0IYLTbL8h5cgEqY7+/NFsevx6Sjeyfzym5xMt0Hittznq" +
        "HPePqH50tvZx7D7f+FUaLyjiiZC1IOHATuPiYMaar6cwOw6fX8SjBsRwVUjhoQkcFOoBozNlN2GUgY/oQ5itZSaEOEEvoPA0JPzb" +
        "J6rvA2iE3xVjwKGJl+SLYKM0nJ70riF/HvrH7nIaDXepEpgR1o6vJBcantfJ2303aYLt0QOUMwCCl7OMFc+LUDRhl0tMwy2Ez6V3" +
        "J9kTvGJ4uM9EI3/xOmzW7V/p0Z+SJUazGx4NdAccPU887S+sM/wVqyPCpIBwwQlZiOzmfRR9x0VBs1f8108xbsNba9ogfkjw2wO3" +
        "+1Fa/7MXwjEOF1pL2M0tSrxgohWHASaCDynWDuATPr1gd3avp2r6TvNmyNhs82it2n4rpaR967UT2oRe0MJpUcPx2mdSJBLEz3/3" +
        "T/HeTk+uS6UuRN7Y/uirczADh4kql0ofIZ+CysF7TNdsYvhYmzLIkAO06F/gVSw34ENNYpR17GwG5R3OL7GaD6kSiVVyH/gH+8N3" +
        "Y3Lsd1ks77L1pR6I6s5nZ6clEvNNOzRzTRFGNnxS6rb3jsg9o0DINB+eIH/g5dbppbcTPu517TEbxeY3anam6bUVRm7fx4cmhC+b" +
        "PBd61O4gsCOBYBMcHrhTIa8IzvyKSUYcF3x+0dtGxjnMIqjf8/OOOSO7s/mH3hnsGRzAtFQhkTfVkBDoCTwG8jdhlZMEcGXgllwC" +
        "T1XoKiSAcaq/JTvufV+HK9P5mQYJHx+EPPptCWkVhANRSlNQlgm1JFlwHkB88R//W0zEPNC8MWcmAwJraz2EVN1NIzfhg4Q3HVLB" +
        "5bu+ivDhGargMpRjBX3wy1LOG3EMPBZ+AdIQy0nINVkPvhW0sicH3THafRppDPOYriNWd3Gbi43jvoBpprQDivEmsdHod0XkX4Ig" +
        "6BUme3/U4i6Yjy+4iDh5A+V0XLX25KL3KLojtx+pXMJV6bMdfe7Eds+vdRwV9ACd60R3z6uTEA2EU3hEp04YRrxJKSkgeeT+PVVH" +
        "Ws2YCTYPYfPis8b3jR8SkSUKAgCgkmIi0MdyXoZL2fdBVYV2xEKAZgg7gSnUZ9xySCbCMEpFIVJmYyfk1+kQR/ixSrYNZyaQhZcp" +
        "XgFlGl1JeYbgSChoDEqRoVvi50TYz5zX1mVyt8oW3pYTedAo/sbP3on0l0OhbefUoRLt1EQROzKBXeKlDc4EuFTwuLCb2vFHXZyV" +
        "cRAxGu2Kc/RXfXczUP57+ZsCZeTwHCaPppsqSjG/pN3A1ztRznl/jNLnUe3GfaQmT3YbiamEi9gflJIaJ8rNh9Gv8LSLsvcMI9AV" +
        "5n84LEi8b+QXDsTA8oqTgc7/Q0SJP4CZptgTjPd2aVEoGyxOliqJcNiH5114xX7+Rg01/niEbGRxowfFKSCm2PVACE5BMvEi5hS5" +
        "3kbFp3HWdLOTDEvKr+CAECeQvoDpOJng/nlYdFKXyiv2/zAnO0T7rqr80/MYeguJmgQhUxou5fjjsNxylUvJoAO6FYg/evUnI+M4" +
        "BDVVUSKnEPSN8K81MZwDhoX4g9mYqDCy+FxFh880rUUbBxgpMnJM3WTK0cQJCae9ODQeL/0QQqgI7v1cSHD02I/TifoqlvnKsuZH" +
        "GfEEacuRpjjGn7S9QEoPcDIUDIba29qTxHmGYirSwQ9ELGia2dWy74w+x3rbyg7FVyWpFA8dFTHvU1AnNZbu82DMmXEEjGiq9XH0" +
        "OhGI4vnQrnV0J2Q/cvU4SsJXC6i3wX52uBBuJqOWUSASyAzaBNYp0mu3WrhxEk4C915OEtF98nz0i1gKwq6hnnE7Brnx8YifCvta" +
        "RrMpZFPOBrCKur04/2gfOy8ZXolArxKhxPnFVKWYOBCea4DMc3Lw4j//wz8fR0iXYnXF59dBRlS8eHHydkFqDenvdl5B+iTMILaV" +
        "filMeOHFXldFEEex5qicsI545/j+7HLQcKRTbxuwqRZfxdHH2mYzKV+HpUR9T/yAacSbWHSC5grOe9adOEUsnzgZeQjoETER0xQI" +
        "gWrnkMTZh0CvQo8PYdjIMOJT0ItJSNe+q2nwGG3l5oChh1lLvwmtjAQYLpTqOZSJdHPYtGNI6e3bvB14vDc0UespCYAdi3xK9Gia" +
        "nW0tLw8+ScU845cI30gf5/7Yi8knubGEEbdBtVH0aubzOfeadjERwvPg9EpyE2Il9a24cNA5chaG7x0dQ5voIxFo+aW0ad3/YFRa" +
        "CiHJRVQkeFAYCWpjEn/4zb9bRjS/oFyGIOFxgoNQMxdWxma5FxZ/3adXrbOAPzXUP3hc4dVKVi3jtb8tIBupbcSnjxA8OMfnwxif" +
        "hRQ0ea3ammeCLr8Gk/ntCPV7uutsOoZ+FENCGfGjxu7s4AebFjICcB6SnDDBPAOjBGZCC+hSsV66PlHyHJLcexJ1IqfCu0903pCv" +
        "yAlEQpiCraHiYBI/oJvVWhNFkrqDH1Oef6GR8FNtrxb6Yi2HiCIb5EyeMvHTwK9I2LmarMXN3HdOOIHz1OUn/CmmscHks9CWZBL8" +
        "DFOF/jn4+wBKfhqd7YLswGZYgpw4kCWogjeEL0pMPHBrdyNcr/W4SPF5fF34/iJ5cHBBWNcBLHjitA3iv/jq35UMxaMRB4ZPZI4U" +
        "EiquOc2sqmHY6S5CmkD/V/wycPKlTBPR7ZgcvYrlWbI4qcMGDYKVvtobdBCQl5jkXjRT/9UazZPY/gYO+2BC+B1kG9DvWSmsB6ge" +
        "3CH2SSbfsDjHtFgClpTMufJCVj6C4AGLPMQ+VF6Sxnk0bkKqHb8EGVqEFCcLjud8LIPtuGcYAwfJrgxqKbQRUUTn1K3jSAg8WK/W" +
        "sLih7kQJM2jMdfgQwC5FsZaRxGLCfIL2gWfAoIS5x7zGH+ewmKJVmD/xcEa3g/rE0+B0F77rQ5wIVliegQkuE8zqQJo/IOKE7Uod" +
        "K5EU8mdr3hVK/05MDVd/0SrApvWpC9GVPkTQtk78s//1nwtNTmJmQ400bcPrmU1n9qBVc1Jp57XH9xtxOPHXORlPL51PBWVLwhPG" +
        "zzgKbDH0jfNHbhbGA+dCX651lMI0xaOH+4cgFRrvIy2ulRg3XgpIHW8A8bWyJVSar4y4njKXKIrQ9SLEEG4x0hjPfEjgWMlyb+Nl" +
        "0DZ0QnZaGEeQwoJDDyDugSO8z63ZBC2ETMhob06h8FJbbFsfC9E/hVkrEi3AUDcPosFy4MXKPF6IyVvbrV005zHCrZEK4ZUzuydU" +
        "issM9l7GNXQj1ohxxx5R5yLJsW5YpcJeiCryvQpfDMp1sCCHFQ6B1BR/7EZ24su1KJc2KBpH+ItRoZbx0ZjOvZKgK/ccoM1Q/J0/" +
        "+vf6HKIIZiuwXwj5C5/3gX4i28gmFSKCbosvPaczMTHh2IMikTCICrY+pBXoDY89nBKfzaVFtkuTr2R+1nxEeaL9fcgKMWbh7P26" +
        "0OlEdFORe0bHSgsw8Nz74pLmvxOUABWAAp8SXCSubtVEORWxV7i7E5MrO0Nz5Fh1vOtkVvJ9gmoP5keXGj+r+Nc7o2KRlfJ8tnjA" +
        "Vpi8V7ThuFQ6xXblEilyI+YTbpHrAr+6HPLIPQuNGxlq4UfiXgyLMC6o21PShYvMDUewkThfsTxBloG5pKSE9C74GR4KLVEQAN3x" +
        "+HutvQpTIfOEuyCkJPNM58zLr8KXU68pfL+P9AvUKTRT0aZqXqg4lyxwYNSpFH/v7/6qeMPrOxfnrijdntUz6UKFDANPoFHiwYlS" +
        "YRsBtKC7UBO4k1sZHAthFqhilCnJqQhl6COXpKKCMYAoggRCWDPF0J6DXMBiIpZJOJbYzUWmsOuw6UBn4ioVaUOcBZqEppMz8s9B" +
        "xcDT0pJgsBSP3EnZPmq149YKdJAc/Rjo+wx4CJcuDHXohSKgfIvnG9egTVvlZlBM9ToRMA+Dhz4OSxTJC5xrsdK8Sv24i45e7HNO" +
        "zmLc47yDYYTzll0mFop1ANY4YaiQDpdhYVBZKGY+l/69lW6P6ZUPS1hakQL8NOlA4spJfOU+aTzX4etexVdeTP3mOa4QWYPY46CD" +
        "UxBLUXYOUzHMZKdQ/B/+mz+ZXtnTUV56c3jQ9llN1vaQ0YJda+RhJ7oEmibwZ+ctY+PKgfuvdSlgSuyuuS45QxwCU4/V6LWRIbcu" +
        "8pKVclR0YiudUzx1KkkBXiip0EVQWapHWKSop+gVzKVPonDa6a6jCx+GWcivjOxJBn5Rmp8o/IY9IjKA5Z5FXYXkY9fXAW7jdClp" +
        "ovaWe++nQSR7fLocoyuxKrR6YybL0PR4akOhRJuHsaVrg0XJfC/EGZSiPeMx8b0JF3sMnoclJBJDhkvhWyMS8qDFKHBI+SoL1uPw" +
        "I7UvGGZ4uw/1QD6C9n8UxUv0kPPPauC9+OGF0tuQ34VVRaaTzTK4mX+QTBFlSMOUB4BkxFVnNnsuCcX/6R/9yWiEXQV7lN2DfHPT" +
        "jrV0VkgNL1sdGGwdtiWbBYsGVSzXEf7/S7avZtvS9TDIb/i+kceYea650g7dvbt1pCPJVUICqVDZBUZggctgoAwUXHHFPSUoBR8L" +
        "/hs3uAhKp8/p7p3WXmHmMPL4wssFz/94ZiL7Ox6jqDO6Qk6Rr1rpDiIdTCWIQ72J61xrNRU1s6Kgjm0aY1SrIZXl2Fc9+QjwzlOK" +
        "GYrkMtfGxPIlhtnYJdNhGpnPfSBnne7k/MIfGuSjmeyGvvJJO+xKsA7KiGedTEqfJvh8ljOII0AjKapxEndLO51AYtXhRE9bi2dY" +
        "VAEeoE4lGyD64GulrqTHVnZtoB2d3/TdWG637BzqKYQzX5dqFDkagW3xUNK1wFnI/IQXotRIhNCGtAD5spfNYH63U3ZCfC3dSclE" +
        "bgrgxF2mTl2Yz3zoESNvJ87H2JVCIZgJ04XDDIOQ+b/7wz/PEnf2wede386HuPHnS5D0Un/Bci8K5IJ0HfJ73WOuslAFFrKTHbXS" +
        "FCpI/ekz6jNVd0O3MOGY4hRHXtfFQNOhEAXC+96CQQEMka4iGAYqI5mEviMk9lHurcfEQtH5z6yXIqm25zbcVgFarPbUHcTVnqd6" +
        "MkP1Th84AIthwUPAdhwExuUvzYtj1UurZTQNwwRHTKzVYHD3E6id7kOL19JYj5/8ZA2XHm5IbpdDeRdcXqg9yDVjkfLLu+o0hnvk" +
        "lGX9hcDBK22bgI9Kdbmfom8cHQqYtOhHME4kSKCK4JMDGtFXBcxYTkBnT6sAVG5jhIfHUByloXQKTlvARxpF9DRvRxdNoUTfAcSK" +
        "IuR/9a/+V6rh3Ovg1oRXpkmVJy69etn6tHE5oEtwGODsZJSr/WBzw0Uiy9RKxpsDtQrvryBI8CFq0ZPSmBOTYyGAfDh3rt0q8pgA" +
        "T5auMlBsebXsTwvjIt8JFGdVOVqmQ2WVCgTmQ1Jy7fROy/YRxh2fC6x7WRzNPXTDwX1wURZS33nN2HjZtG7WWAaoCx0yBx4zjSDg" +
        "N7h7scb5zHJg2Fk8u8GMUVu+S9T8rZ90pmr5hzhYjCFb+qVIKfTAZiz6ZU/oYZnJMuytIjMXfWUPI/E19RqGqUw9ksXujPAodoT+" +
        "VkYadUuXgfPYTZc9he7HdUCCk5GvS3p677qjv71WI027zg8r813GMhnWhU00qm2qph3MRn0XiO/50OjqzNHOPwdwO2ImxIvXjVzf" +
        "qfXapILJ1+iIw67neNhyuKgwPHj1xCqPiq9AItv3CDWlpxiPcOTKTpvJMUsZi0p9r+1oZt98xMNj0nm4ZG6KMEEXgu9iP8vtmWG7" +
        "9OYJ6y2WX/Vtq/tnPk/FTaLHz4Y/nOdBVxElCCbWbe+yVMMiTiMOalEE6YQsAJS+ZcmI+0INKWSWS299L0PvIA8nBSgjp1DrvehE" +
        "wEn+4npNxecg+AZ3JPOVo9dtL3LeQdIOrVY/fh+GB/RzGUVEkavuervRtFPFnMZnCLeqGUmxHLRW3AD+FLY9RkuRK7t+r6HGauFH" +
        "MU8Hqi6SFTqPScZVNCgMoJoO/Dv/05+NDY6cq1N5PoXDgbepxwEX1xSv8HDGXe2zjNu1IablGz2uYTbu/J1tjuEzI4/g0qDeyexa" +
        "YUCevIm8Ze8dkgKnpAIDCl7H2pOYlmwMxcADwbmlOyBXSKY8tcQnHvdOYtj26mOH1NON0+vAtyxNALjxh5PXgabBoWZGCTozZCED" +
        "verbHnAzCgoPReVKD20vwQCd8ZMSEXF3GfDR2hRwSaOYb1uKwPc9Nx0FA/RfIVuxRzQlrBqqb+Qm8tGFB8txZF3q/+GHrPMwf2eT" +
        "uXdjO9eOWB7Hzo+d2SmHGDJeW0MWX85cPsFd6IcRxSfc7vDZ23wLxRmzV/AQmsmTvhv5xTenPjO7MpoICImiRn0ay7+7t3SOHgWW" +
        "XqgDs4RI8OP33m1sHpP0Tm6DVc5yAcrtVdU/b+KD17mC88Hplub3mNYwMEcS7nvbepclThvlegQlemKMccM2MNbTXp3Oojrfhj7c" +
        "gn6EdAEuZhrbsPPyb5PO6mzk14VxvybceOiNIfPZyrcusGmQ3ESouEfkzupNX1xpO9ZX7fCS4EmzPzm9N72i7tJKNcRpjF9AzRGu" +
        "9PXA1kt/Z/c7Sh/IIRyQ3vJAtfzwSkc5thqWnWQXKscQ1Jx6Fw/8tE/6DG5nTgOG4VBlvq447TgUeTpBPrM3ByWRbM5q/n8P0cTh" +
        "N8w7aX5tW+HIeQ79paCFpTd/B8fX4H6/e82dXQefm9SBWEOxB/5n//h/Q0E9cPlDsN6CIeQScsbzAfbkBSQ5ubRx14HsQ56NJMpc" +
        "/0nxFiCDC9Ell3QKwZkyEYyotdjXmMdAuV83boTaZSYvYMcmQS61CwrhCURIO4RQ4yr1miW3Jsns+zazZ6JMbIBPR1Oy9RHV6KiT" +
        "SOvJKFzFEKO0iHaQVqG9il+7QSk4GtUfpClNzHQY8fG5PG/P33AQjCKT8VUYwoqbhVvZADzuAHLPsPazzOA7xDX1G+UGuors9kYS" +
        "9F2ts8jFgbWDqjKpAxkdOElsErtqUDAybcvbZ+XZR7lcQK5L/lXvE+a72kUj+DtWqiEOoR2kuNF9iJMRhhOY9rzIjQr9SYInAl/R" +
        "FFFSx//Rf/oLd6bmR7VdW9i7hdBwhajwNEikcDP2ySTIIi62fdDD1Mv6oMDhVJsI/APq+wS2t4Nb2OvCtyk0ofRapqi2L4AanAN1" +
        "DMwmSFHZkQ1D8ZHrO73fw8ijntOkcWHqgrG3H3mLqgtw3Emdwm5h7dS6lNqZ78c8vY2yPACLepC+chahQOiIwEDyUANBnYdtzCPE" +
        "z6f2i6tH0+L1NJ2wlPOIAxjtBBV1MUwRq9kQjvGU6m/7vn/hLmCaSpPjau8ygLXmMLFx5F2jxGJY00vmWcHPalsrrLrAr/XnkosM" +
        "sPC+pXPk6hdUJUYZ56lkO/NYYXqjLhEW7yjKsb0x4xsLRH0gqw3gF6VeaI+qXpq0pvBZ8X/w81983Dh/EVfZdBUERlZnOWmqWRSj" +
        "cRIy72ufdzIVRwJWsRaJlfgSOCUMOHxgikh1aA68L7Evwe/QWEys6pSrxRYRr7SuvUDP5QlNB8W1ZLe+JUxyuan79TqqS74ahtOt" +
        "iuduaOh4gR69Qd+ebVEEqMns/VgzLqgveDbjYcG2wD2o2Yhmyr407DyejVyMqWd0P8njcZBrJAdmweYKIyHSUngMgE6JdYBZgYox" +
        "ti7s/SliPXXhiZRAcdNuc29rlTupYmj3egC8MX74tfryRcsnWnpqWuw+ozmQ2cIJ7E0RqBhCJ58++utBtMMkgSL1x0iWAVSBmIG8" +
        "Y0sUi1hD4x5PoupYyBD/UfY/KwfdiKOJWgW4v+EsxHrrPYBK8cRWV2gSnBZqglKM3DMFyspMWcg5cnKybCfojzR8wm4t5qORDlko" +
        "R2q0hC3T2BXXvqsp6HndWC55FLOuefigrpQ0CV4eo/DZRhPItIstbl3wtAVTiT35809VVulwC/7JcqK+JczH7vINWC1BT6cB8Vay" +
        "EVW1toA052pBl5yiG40F3YdYzhgrmbYCTC6D7NZsY3kL0owtXnVJKrsm7E+KH2zx4rgnUDiPhtzKkwseCCZPyn1iH0l+YwZF6zoI" +
        "I+m/Ft3AcMGGhQ3U4jKrrjR3VjYvEiJeJYgEeQTSUWyAMn+pVc1SBJ4yoUiqRqmdNxVDh5++9PxHv/dXo1b0IJEiNcC48RvNJqf5" +
        "knoFh9j6EAqnnKK592HrKqf0lEbgyjPWW9FPQ35yHzUj46DhkPjXwH5BroGpgu5KlgVtSi8DNXtgR2ZseKOUpeS1G3VoS7qsICpg" +
        "6Y3XqEWeO73tfNe7o+261HEl+ujeJvn0XicLf9MPhPB87/aIhacidwfNYMgrikD2R+dDREEdwW2MF5RDzgstgLgYWfDUp6IiP6nV" +
        "CbHcBfueSKOL1DR1CYvufSG9OQemDZ4UDiHFRNNG+ESXWvcEOQBb/HzlvIemFxL0gtk96hb7E5TKR4bHg5unVmV4bHmGvn5RsMf5" +
        "WKpIUg/PPa83OO2FNX5+GZwA3/2Lvxp7DpCS3iOjVLJwHq7pXOGwkaGVClxc0TShMMIIxSqiVoYSbAdesZ3rJAS3x44p1QTfoSwx" +
        "Nvic29cVS0lhz/vItYMDxDjC+iRq5VexKgSOCzuOwRJQLpyDK9m8wMfPZvd4SU8CiTqlphu7xe3461k4ZolyNwCNP8PxQe+OtMwl" +
        "Sv2PW+y+yLjy+6Orz3Z0BBlwHgTkUT5InAiuMEefRnbrdFIy5K4fuF8HRokuXJ9KVECxcLXoSCRsnG/AVXiweiDwB5iMPBRQM50P" +
        "PniWKw8d8Gnsgm+GcmGu34pM7EaLODAdLN7J/J1kLKcdkcI4kJeQ2cH8e2+OGju1PgFMMM34/OLPJEWm+Q/+6b+uMgoHth6ixjLK" +
        "LPTdBjYGm2pIS+xiHM34KqISYeHcblD90Se97R0m1l4V7nOi1w1+XXj1CuVAByN4BNcjjqhvYPaTt0d4NOaqilrw0RTnI+zH/UIJ" +
        "C55jP9Qq2hGueX1R+/ft6dBUCm6CkBy2GpK7KJrR1QjDzBfef+kDe4DuiKzpN0P76Ul9PpniNXtACKiesQr5eq7nb+3n1Bmmf5Sa" +
        "JsQul3yLbYjpbRuw/GogriipWLXEuR/NWwj9S6Ajg25NqjTNhcKW90zZBF4Zc671sZYPtfnNKRVLfwk4yEEbtGObB/Rc+rK1lZXX" +
        "d/z1lX0eVLvXkYeIZDgjtPKBgEp5dexBqM9Ub6RZy2XbZ50gE/87V/9LV/nAwDwHtwrCEPNA1lqft0MtdgZqnGoYUXiCPJOL8OnB" +
        "dcaPvQfGUSr24ssvEr7Rd1duu+FjCe4k3cUvDRaIP0V2IBjtoI0oXNAVB+Ov2j6yI6tPSvKT9oIfrA3Ommr46X3VHvuUmaZxsYiq" +
        "EKMklBLmWi+YVYtdx7st2YBGESRfS9vilw9yt1BuipcIM+Kn2I5D+mbswcnLwNPvBkpAH9gBDgpvWwOAT5v40qCfesxkkfjVpO1j" +
        "qXaxB3hURFbFraAIhgQ92haCM5mjPPzUjhr4OiF9cRzjMHZdIq7hpx/Q/IBN6ZYY38eMFVc9hSvXhTxUpL2UR1AC1Y0aAfBDSw/9" +
        "AWlNtjt39w1OA+Y/Xv3Z6iYa7vW4h2HnUw1c+4eNOStgxJniBVKrUC+BN7DfQ4WwSqhYEMyUN4AJ0Sq4Te3O6U8HkMo1zqpWJsR4" +
        "8ljCMGE74gXqcA7Fytx4/BgY3/DIcz3vh8e4Al9Frv4MT4HJ5nGOehYoRJKpOmiXF7RcqMsjLBs4WWwszkOZXllB+vuaPePbAXXs" +
        "P4xMUvJ0Bvb3mpzAVjrKIUXpAbeO8jUODaW97z/yfo9xRYVhDmE6NuFZ7Tfx0VC8V5bAXElpg1njrJVjizmi9/D81G4G83Wg7rVN" +
        "ck9C3WNQP+PDizVOglfAK8jneF+4kiHp0bfUW5K5nC2VF3gd+mNMl442J5fum9XWHAJ8nshbCrm26uZ3stjC8bM9Ai6vqbRSrZE8" +
        "FYCXBNOQQpJ3AI+En3McMVCoBxY+DkMrTaCubTeQ6g/Y7F1j7VirU0Y4oukKn4FkD3PCaiaW/fWBEkXnISia4Dw1Pw/ob9rALXrc" +
        "w6WG9h/1EwnLEOyOl38vd+zR4yai6xFv1z7OJQyBG8VX3lvovlC98TaUeKGc9qstTHJofq/53Y52AD9O3fgExQ5Dj5tO9RbrhR9n" +
        "w/qoNCE5MgPkOz8O/eUcVmsMDeQzcDOJHA4P2E4gNEHwgwGww0tXta5lfHeTFN9FUVsOe9815miRepdmmC0DdrDDfjq1peCu41Hu" +
        "M0NkHTH8OvCzmOToiga+F5NYl0zTDHz2sVlMVDOhWgv/4X/9b7YdYEw3d5CRPIfsrhXcKnPFaso6ptxI0rpyS3sQ+WLvBwnfwHGE" +
        "0QFMgLPYDJqfTtxebE0YGElO7qtXKlnI3+58RpQQrWK/nhuVQvjCdYVVjwtS0lFSswnlp6G7KwKdgVEeE/eFe4jVu0D1hDLHugXy" +
        "+O61XxP6DemtWIdry6GV+ZzUDeiJBAszDdlftSY39hB/PkEvtGCIazw4KsYuFAiOuEVlO7k8WS1YjEHv/KbTHIoeQwgQVDhEUItM" +
        "e/j7TtKD1MZfiMaIyTSaesgO/WUH/U6Gl7Y+D4wYZsGsUAmgyWBqVVmpZD402odnTYFYA3/TtXyBhWio7WdyoPA6Ck6trRiXszwJ" +
        "efK15n8c/xkivrrCZgb1kcIatRXI4aQFPVCETYv52mJAO4H2Hn8/ccXg3kcBTOV6ZCnDp2e1tkwpY0bNUo8m6jfAvnwvfU70TkhJ" +
        "tif7wL0lryBiHK68MpAGMmSw/RH8CVKrTo8SPKvwQZ17l38DcUFBzU0LQ4jLa3EvdPxM58YvBhBPSkP6DXAKQYtFaDn0lFmu9LYN" +
        "OkecW75r1dRJ4V0ECuDhyMkPYj7Yy9a1BK8IhlIKlHgisKB4Zs63Xi2c9fDcQ7TFh7pXxInHYayRIDz0XTWE+45bY53fGSxQpplS" +
        "QMphWEM0cFupt4HH0H8PVnkuTup0pi+25yuaR0GJ3GjoUrmh8DnHu/us0zSPMDta/i9+9y+v7lkBUI0vOWkHMMCy9JcCu0xcBY8X" +
        "d9OBrq1Yurmhl5TmvzajEOSNXC58/js6HWVRYJuxC9E7KK6wtlQd4PdD305xd2MOA4c1TQLkGK0HbugQ+jcVftrg57ZVe391CPoC" +
        "6sS3z+a3m+jdW/h+0Vc1XzHjypmWNiBIODCutOQz79/4qfHl1A2ZX4oL0RlLR9BJ6OrMqEoP23Bk2BCcHZqKv4A1rIgoUDS50S6h" +
        "KId86u0Z49oXWtpGuwY/H6ln2Z77xLCd8lhrn9M6QysUOywKjYkaetBxML2NSiHd2JtcmmXgB0yVLNjbXehzae4aM/bVgMfGzpPQ" +
        "xjwgVsbxgrWnVapOoTiFOqCXZ6NucqcGedJxVFtssQxxMsO+x+z/8XaOm9TBTMpVkG4lG2DyK7tO+LwMXj03338Mf+jxunWrMbUC" +
        "3ZchFph8HeRT80NC19dKqv67i/sAtPP961E0+uK1hibj+dTLG/sPO67/DsSKucaC1GBx2zfRd1yM8O4H/ilVu9VwdyR8ZJvg7K5/" +
        "z12xTyzxpDGu1k3sJxXul7YcIHoOUiPTlbtc9LBTm0GunIqcDAcs2Y8B1MRVM+GzjjUq56uaZp10DctKksiqI9SOuxfrjz2L62cw" +
        "i8K+E0oxTqFP5TjRuYSRcRURJU4hyosPAwzeKfwo82HYfMtqZIfBa/HLOnh+r547t/lUK1L43rvGlG2fRtp5rEPBtQ33dv7z+JfG" +
        "Ghb+r/7kr4Jezhd8+rFf7U0Scxjh91uPCPOBXEAwxlTTy0WCvQMPwdFlF3s8QfOlJwAgvO56y/gwDVYIr53JAr++9cbSlMV2tL8E" +
        "p9R2DgYDd1tZTNxo6h9/yU9Ht/Fd0ajRMqqNHd4bnatVFAZM/UC04zSmx8KlPb1Z9tHYHidt++o0DhAycIiZg2DeUW5fWDESVZjv" +
        "5HMV2gv7QNICVMeuo3xs7GqIJ+Y0qZO5zWemjqTLxUU0jUzB7qmKhi2eX9xDOyBCqNV0Ep1jyZlyjVeB38xdl/rrKwwWtpgZmMF5" +
        "gucJfx0N+0FtOVhau2wGG1PjGU782OD5Cx0/98TEGvcwJGeIQ5Um2hCEER0C+fpbnTn5Xry6ZVXPWLX4/gWjRTAbSevk+EujNCZ3" +
        "erO3dwc8ZnRUcky8Wqnw5CnhsgJ3MZgp1brIY3pFoCQdoJtwrrqodqPvi/Odk8wdTDLsrXrveKHKGbopXnX9//l/ULU3q5F6mVjl" +
        "VfJ3ZhO51W04jlU/eC7Za1+8c+H/X4X//R14T2Vy46IPvdvMyqILT8uWD/qbi62FL6FtSN2PqdtS3oJJJe9VaH0/N9PQCIsXNOcg" +
        "AlYz83hkfdBxJHrZ7ntSP0XO4HGqtt7FncaM0jmxoMzEjEzi0RjiF11cD9N50yKqHXcHjSfCBA6sM++a174Fy2LUYyAhHXvCI5bU" +
        "D7EPDEEls7dh9Z0knZYeliv/YO3UMzC8bHg8oJ97/g//818Ma3wmSq7UrPV7S4+1fwPiDR4IlyF2Gy8n6b0kOfkxubNf722mKQqI" +
        "r6LJO54krhbVZWp653UK5hGpp8kYTl4/fpCP+1Z7/A4CdUOOcfe3ctzbuFBuEAn4tPS51RPU+UgdajuqaJn7YOZPz0qv8T6UV2p4" +
        "iXhA2j5HUZXMI1HRMCDsGK+t+FP8JFQrHw/KiGoRhhLzUFYhpHlvFXlLrfa7hqJWq3N4HuBmZvVkUANt+6Aj4h7PW2nIDwm+UkrF" +
        "2AUwLSmYuEsP7mPIt+76yoQgm03kjkF3pssR39aWc+EalYMkdoeXuBMeafdphltx5UuvhWjKxXc0SlRzlCKicCp8VCoWXA2NxecX" +
        "mOxolJL6/ie4+tHMe6dm+hGwezhNR2Efq+5Xp0nANlBU6HSm68HPK/jondWQ3+lxAMGVeA2B9kfR2GDRS37wR6tJ67nrh0/0oQx2" +
        "5DmgaKaVw/mv/E8pBBOVMbKB7JWaT7Aft0+z4dVjnJXyNMUiluvMfGyD6u0Qd3jvG3Pm6HP+uUdJ5M3PLkY7einQEUzaQ4qddrlg" +
        "qewht/EmBoFk1icsvqPgovfr4J7cJtUdeiLIiZLE0lGbh/jSoPJQohwqGcAVLZ6UlJUZfW/DAPOVTtf6b2P5rdyvHvHSRccL7g4Q" +
        "VtL3nmNS2sv/ZTvg17Ph8z7tgSPjMufTRv2Q2Pp3hqKLJp3Oa/rh1GS9TjcMKHADxaM+/kq1CKexmUR4f0b+7d/+iyrBcaDCQdrS" +
        "tnlQ1PZy6JxWs1SFhboq6Ig4JBQ7oICSN4S/JfOVy5UrJ6haTNGuk+DkKBd/2OO0t8Zg9Wi3e5+07hLKfRg+9W5xhr6XY9lx5RcR" +
        "rwjOIQFRNe3dvSwCPb+x/dsh3OgitTTyT4W/b/3lp+Txi0ob+HZl5mg/efXSqJt9qD1jGcRntel0kkAdmiHwr2PnAn9xZOvw4YNq" +
        "XnD57GlLZ8uhIzzxnHE/s3WNwxaDNfQ7rxuhBR9nXnowjKGi2VRRL7bxq1v2VyKJyE5tKjyDYwOFgyKA80n8U5+bYXyptYeLDiqi" +
        "vHVyokvIrUiwsm5h9i8eG8IYI4tTwOQoH04+NZiHzBptCudH4H/yJ3893OHkTj/PmFAZpIuiq1lop9HrGcWvySy5TJjH6HMMMkwb" +
        "nPRiRxANMFRqU6nJJ+sv5G9wSHAXMDVAR+c8ttfhxclCBU5Qpzhc8eapGYy/mSSzjAKQCOAcchwTLIboxiw87gwH1/1c/PMpzgSr" +
        "hMtjQIXM3tpxb3VD+01uxwOPTdLxwZF7DGcVxkylYxM53MbBT2l91g8fvHmG8ZTTBfqIWqIol/DK3CEcHT5HQ5c5BwSKUORNi+Wt" +
        "fMk7uuA8VMsYjtf41W/13QTDFpsAHgOIY+gWHl/hkKJf+14wTGmeAirBzxUeJF6oUxHEvX8542jKKqb9xVbpEGgV5pS/QnUFR49Z" +
        "RsFXABEIwvpkvSH+Z3/yb6KVnMHXFZSBgIX0OiCDeUFxDpO6b4xqIjxXkA8QZXKI5SvvlIejsD1zoyGewwhdFvhPgbp0YBQHimLC" +
        "boB9RjdXugxgIfzx1J0DmV2ni5nyIapB+q3Pe1ws2SYSBr5knPdMSs5e+56Uw/NaVwbeaYsiUvjys9YWpgmuHQ47PbxoPfEx+LRF" +
        "COlisUlt12H/PeysWc6DsUI+Ch4lvockBwv+clLBVpcGyt62vY86pgDvUoQav3fN5JWevYUsk0XskHDf6Nrw6AJu4cvY9y3SgUr2" +
        "9grTRA+ZmjdD3SkDfK2aibZfouT52d3EGE70oQGv5Tx0GvntIsCRrS9UGlwy4QVOtdgGzpFLY+L/+J//wnzBw0YMw+UyLEib1hPg" +
        "vbOYQtdivDX7M1OC4xvpnyhD0BPxO+aW6hRHuXOM2iCe8Xig8wSsiO9INUBOwnGAjTuUbngZhr1J00gxXhGcNZoSkhDuv/LgEIXJ" +
        "8+lC1QN//VmVEZ4N2z1ZB8nCs6F0J+4j7Gv120W9kaDvdDO1wZUZaekSv4iHkyhHVDfyD5tWNfR2EcECjGAt+Cr3yUU+fC/ZD4gJ" +
        "lwc3PLveQFigFBAFlJ5gWPsyVmNU2V7FA8CRooPfnXSeeJpI/YnXP0n3YvyLm+9oOMrIA9xCU2jcwyiRaIThuX984mocfEe2NHRG" +
        "Wv9Q0TMtdHrXh9WRXtDoFrHm/skHB4g8mQLqwPMf/8EvnlofV+hBzjM/UhoiTKY0Yola97HV8aGfKj96x0+Wh0C+HfeQ+ccF5BaK" +
        "wsjMfrKKPgEfxDtFBr6Am8ZsFqgNulrOpUeBYwaF1uNC8RgXARwFqpi+St2oG859MNby4ayGDbmpvAk8V7QJ0StIESMPlwiZ8VCp" +
        "1+mAe6c2cqFwsicbYljYnjCu1OmkLx9g9+BNLMW9ziu2OzA9Xo28m8EPT+Q7/801H7Z2U9pxGEiAeMG4Zw5pH0FWQUzcjyBiiioE" +
        "C3iWWPtlYX886M9Ptjv07mgmYRDNtEH4zciTx7WnYazu2Cj0dRAJKog1EoSt/PKhKb0JI70aYnyS7KNnoX1mVcd1JGxgVkE64HkM" +
        "/LM//svmbJXGhVd+jsONLQJWU4EAVUunjPtCf+ubuLWnpZ797sVet7plE7n6qzptafdTun7GKsC34vqYast6BmoM0FJVsE3g4mSm" +
        "+VTg7I2KZ9Ba4BPaM8yvhOfYbyg52uGL2x1wMlE6BR0LdtR0eNy4SYuhQe6g1oi5JApqF6TlcFpLZOlb09cuPJ6U6/jpoz+8DNMj" +
        "yoDNF+PeD+OP/crKKFbtGQ9zyH+T6gouDU5/Qx9S786AIY5G7DtJNbxcybykPMRzAomDcCo0xdnUbE/h+oBfoLUk6SL8ahQNVmBE" +
        "q1h6QhsjBOBqHm3aIPDPYYoXlzj/fm8ejuV8CEbLWN2oHdrRgKMX33gatCiLbowqotHW5474D77+S7phfU+uwq9adnNPN0OQ2UeG" +
        "oFFQYTiHaAqjxt5mLUZYO30sZN6DHdT3pEuDroNZQqogf8J84mBKh9BXnU9esK99mWNyhqsEgyuqBjmSj2KcXUmEcnigWlS3d7wb" +
        "prHKNCYjr+Z2F/PnZ2s7uDfYn/zCeTAoOVUt48F/eXFv6iZaKWmh/BtXCJ2APomp/BAebNbSeSQDSkb8c2uDi7XM0JIvYU24mLDf" +
        "+BP6au5GXo08V3MXnEkcBteY9lj1kEewUNakYCp11vx5sAYlvA3iiap6n+5gCjgOHRqoHa4NYk2zTIqm3dRhF/BhbY7rCsdRfpWP" +
        "KrRPpu5NM/j0ZG86HGaMK4yXsku80zzZAP+TP/3r64CfretG7nVMxUahpcvHwCtfX/vltQlWhmII77vcGW9V1cfpXm00D/8QD4/0" +
        "XJuulTc9u1b0BCIrcMBjhc+VUcxRj8dtFwm9LT2d5MgMNUwJuwrLB0gAVQSHb3n6lvMRJGNXsD+/BJ9+7Y/Oz3+LmxDnIZ9OLvuh" +
        "K472EXl3sIw0iuVmczrlsbsPc+u2R2g1PE37aB7NF+mmaSnm62+z6E5Fg/DFdVqRIyQ4PhttMLMMMZQ3do7K9tBkMkrptPP2B1cc" +
        "YTXztkZ8jzJAs5UvkeeQoJaYqJsIjngpkjn3vKXsyUKG/A7mmWzbyO3c4WhNrOqrJAcu95Uune0cVt4TTGZx4mS8c2Olzga7TtoQ" +
        "ZKz4D/+Hv/KpCyMY/0a3DoUrhWc6E/ycsEtlRN4AjbM2DYdyzO6kXy6x37H5JX0YBF/E7eXqjepCmHWQGt94bDw9fOrSGnGQczVQ" +
        "SHeLsA/xurHnFg6dm2/QDEBjIgOz0iwDqW8RAohEnl70l4++3Jk/nAXqWj7F1mxxe4acMHiq1abbhOo253AW5CPJjt3+wF1HbSfD" +
        "yZrGF7OoKs2ZzNub/HYiroBtHKoe7db4we9OLohwWagTwLJVwVQuE2capA70mX70XZro765hHphfQhgr6F+kP7iglEMgs3GQp9QM" +
        "XizkmqTEPmM342+9qVFVe76ssQH0sZoTPInbl63SSiNJM4RKLaaJj7lr3M2UtWDV4aX1c8+jGfC//G/+wt51VLjqKVxvMM1QIxYr" +
        "pyYOOtYIV0nbZy7Z6v4h3V7SocGTIo/EIzxey+wr1j31Fosl7k6UfhgOG7NPQBGKkXGoszdhb/0Iocl0+Wy99XFASeOwFT1mfYvL" +
        "fuCSjqU2H+WwR1yx/B5/N7XVTj8YfxCbZrpyFIU6TjiPdb5iqX1wcaeOYwUN4Ppi2nYYQUgH1zk/SuI580JJeaHtRU5bSUGSmOlO" +
        "Rz/HM4tGWty5kPDDwaPBRjx8cbnluz+2+GrwuzCK/ebeHzKGke56uA70GTyssTk5XeKkwihENQDHKATDI1ZOdpVdWd8EvD/1/aam" +
        "PAy0OpNH8XccBB7Plc0DehfZcqlPMZaJ3E+U7ZH/y3/+F8NZPzus2CVLg7GPPK/I1Q3r3EXK3XB7ZH2yUdtr1QPl0sx8EyKf0REq" +
        "h3XgbldSOXwS7DPmgTRyE0I+1y7HlYFdBvcxPAE+Jj5oMdrayMpsSvPc60QOEMiDf/nidOOjmO7Au5D6npuKj72TEAbrewX3xNFM" +
        "9VPWpT9pFThfKF8LPTx24LzXrBG14nwStim+zZARjw1uGhcmNEpUJn6ZiFP4tLRxJjOC9y+ERD6TU2/bRP7JK3o7bn6V4CmgeUXm" +
        "xM2Vf2itaMaNSTqwN9SMhXO8y8gPgEriSMqGa8B2a9VCmUT166EOyRZBxqq0zlsHmu+VUgW7eZjf81UwhIP/QHp6L8PYxSHwH//s" +
        "f89PFOZYj+24sJu4S8eu2kXXHYS5pWKYGP+wHcEx6D1JDNqAVPwhs+BhGqPN/XQErcdLi45FFNvf8BtwyYlWPelbGBS4C6ojJINs" +
        "rmD/2tFcfZ3RMraY+ctONXvctOBFlhmqFMAA/GjDAVumk/HlsU9PygaQK76pbU/UeMqVt68C8bzeyyngENEyBkwBwPRoplc6ukJh" +
        "2QC0sWCM3NEcJWhccoLWKJNDf1A+BRv544vzxn/9G+pqOQTvg+MP2cUTM/UV9H8LZeStlj6hG9aRp2EJMHMqo87R69jWIZ8TOpUy" +
        "+wbVknYhNvfaOQzHQZWTm6thpaK78GYehU7IwWrs2rHyHQZjymd+K8IV85/+0389QhceMEhwUNA5uAxoNL7W9la6euKPT1lzDhrA" +
        "Yg/jzOwn4BBhIPuuDzTYnoOOvIc2c1Vm54WLEf9tWEahfh3qqJKPWxfVQgLRyUZ71DNuljK5R5V494G3HTcnkzLKWE8j8QhPDQXe" +
        "3/rhuafdfmidy7waDXz7Goc7vDwhVv51MvQDPRm1bTz1ngPFI53Ogv1K5Zq/bUyk4dmrw8ZHLfJY9FeeIkwY3ACpw8orygWVfFob" +
        "SOD+G/VqZirCL8eYjzjNoDTy5bPLEs6BX2SYpIFM4C6GmqU3EJYsGlKLbgdVB+YG35F7L0ged4dBT9lnmKTMr3DIobjHkwfdqTSD" +
        "RW+QQM19sjB7o7YlHirP/97/+Ofe8NT5kcFdGRJTH9j5VS/zYbTW7d9mn01U7L2NAEcyPzjb6y89F4K6560TrcEMGL3QDMAs3YX8" +
        "+v+l/uBcCIu97nb25Lw79IvSdx6uNJjPOLPqVeG+KL2OVVRBdatjQg2AgubRSEF1pkaboe3de3aJwTyNZjf8Mzafznqf0s29EOHj" +
        "TwJ7s2aJQhULRNZz5eJW+Lf0/I01T1RV8CG0X6dqPCZ0UKXgLRuHU2ebmPLU/7L24vDVNY8SGRn80FJ14fs76xP/t3uvCUdanS9u" +
        "XOjZd8Zd2UtD+ombQEyD0Z6KTl5KvD1bR1Q/YfIiHzunhK6UVmOffOOOB4Ee3YtUF7kOKUTIE5eFBhiGk96Uugp8cG35D//bP3dL" +
        "pzLwZwaH0nA0qImmYx0+n9P0hBrF3sjIgCHMpnZ3UWIozTxF3k1dHbiLgSTF0QH7j/qnB3sp+6WJ3QRKgujZtacuv42jaZBF3Cui" +
        "UH5+504Hph/pyHip5b50pysdaKwUuZTfiP2w0EOm3cbUiuJVcnulVl+bR8P8QMUMrMfzJ9rnhE6iTHVjHgYpCIeYv534N4vh+zZc" +
        "n1V+8l3KrwrsDZiK9JGAkcYShDAbD09O1SD01l7FcKnx3uCT9v11f9Wop2e1bV02oh4gz+ire08DbZ65MqDGMPRYKTcdkXPoMnIh" +
        "6ifXxARWEDCd6mwiywyPe/ix6iagq8CnqPCCy9JOyAadJ4Z9EIgjlYgo5P/sP/mFJukze85AMdQaCqR8rdalCgun3w7JyPlYurFL" +
        "xkO30btGJ4HQmYq1r561rblbmPNJ/KNWJ3/QrptI0OPoE5zFNgVn0+ge1MAQjsgHOL8BPMt6S5xRc/CXAJaDRJ8HDEkF2K+gayD/" +
        "ZF8IbR7ISI8j/mpqXYW/qniaQ27goUTTuejsypFeMD0pH8Q0HWud0v148DVWiWp/x8DXPr1zQyah4JogBwy0FOTDmbUNfa60rmhC" +
        "dNKQxl4htD9EodCl8CX7YeSKhGgk0zcGG9qe1fpoo0eYvmCwQv/WGi0BMQZw8ZS+oaEgHrG/Qw5gPuBxDdtPrhhrN5bzaRBDWcKv" +
        "r10QSchOLlhtuBwo2hAS8Z/80V8nPfFe1TUHQC3LpcL2Ad+0MLpy5NAQYuCj0K3r4NDqnNFYNJkMSNzDLKHLjjZJX4dWffDZSbqE" +
        "ToURC9cY6UwlM9XdQjFATtJElBrZg6KzL3r3kvIo4VPOqcYV2TZU4QAP13ToffLZxY1NEUe3zCLHT5TH0KTcfLTti2kZ5zHnE/JX" +
        "VIWorjG2VEx9rF2CNorEOOUzqQlbgqqnoabc4nIymAziGj8eIrByieRniDC1Evr3D0onMkbuP+lA0C0sju0o833N+6OSLayHnmf8" +
        "akXTIw473oc+MtScKMskr6Al9CDZCcqJrF/c8NFGHoqd7B4MBbz6Wo3eDUXmCNEzXSDwITdEcSozAv7T//7P9dRsmeyF1SNUz9BX" +
        "ML2Hm+shbPHognHnY5L1OdxvtGZoOywcFIk7TIhyvOzd4YubnfSXom3uYRUnBNwkDm8pS4PeyU1DWQiPK1m0EGjJXw0POZ1mtAzA" +
        "jmjX+Xwr0ZQT67X3Psdyi19WzlxzrvRc47KxdcqnKWUH2Z/lSzWMz/b1PJBXiloPz04X2McYTfxUeddy7sz2SzjdCXRB16i2xp6l" +
        "jN3U4quzPztVPStXS5whvzGLVVtdgg/vVaToza17HJthNsxCgk5hx66lqsNW+z2YXOlpqtDiyePUIQe4Y78oAHIoFdCj1CdIaum+" +
        "t5cYZaUkpNr7keK7V1GONHdSxwiOErJu4Y+Z1le2X3lk4X/5L/7CO/z1gYYT+ACjmPQMb4xAg/7AifO4sJd1eH5UmHvfE0dCCkZb" +
        "6AY6lrBuvY8gsTSPo/108Ei2lpjVoLxrQAEVGu+d1DlUGr8SQy0diM3U6YArLx+df72gLJIhpgSkPGIDtJjQT0mf+yAMaRZZHuB5" +
        "UFWAfe22bOd5+DO0BulLyKMUCoeCWOS+FmYAOmHLKg2cr/CwI7XDhPjovSK6tlJ3tHYcl+61MdOpe+7DH98T1/h6RMUAVrAkMYjH" +
        "M+gXpTo2Ft0B/dQ32uITjU5cJNBHEJTQBjBlOpXY7WBt3bzFnfHx4K8D3WRoR9DG8Gqlw1QcwpXzUDJpH6JvP0aB8iqQDtFH8v8B" +
        "wHHVfKUFP50AAAAASUVORK5CYII=";

    static float[] _r, _g, _b;
    static int _w, _h;

    /// <summary>Decodes once. Returns false if it could not, and the caller draws flat water.</summary>
    public static bool Ready()
    {
        if (_r != null) return _w > 0;
        _r = Array.Empty<float>();

        var img = new Image();
        if (img.LoadPngFromBuffer(Convert.FromBase64String(Png)) != Error.Ok) return false;

        _w = img.GetWidth(); _h = img.GetHeight();
        int n = _w * _h;
        _r = new float[n]; _g = new float[n]; _b = new float[n];
        for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                Color c = img.GetPixel(x, y);
                int i = y * _w + x;
                _r[i] = c.R; _g[i] = c.G; _b[i] = c.B;
            }
        return true;
    }

    /// <summary>
    /// A wrapping nearest sample, as the material's default addressing
    /// is wrap and this renderer does not filter.
    /// </summary>
    public static void Sample(float u, float v, out float r, out float g, out float b)
    {
        if (_w <= 0) { r = g = 0.5f; b = 1f; return; }
        int x = (int)(u * _w) % _w; if (x < 0) x += _w;
        int y = (int)(v * _h) % _h; if (y < 0) y += _h;
        int i = y * _w + x;
        r = _r[i]; g = _g[i]; b = _b[i];
    }
}
