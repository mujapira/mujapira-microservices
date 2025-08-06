using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Contracts.Common
{
    public class PagingDto
    {
        [Range(0, int.MaxValue)]
        public int Skip { get; set; } = 0;

        [Range(1, 1000)]
        public int Limit { get; set; } = 100;
    }

}
