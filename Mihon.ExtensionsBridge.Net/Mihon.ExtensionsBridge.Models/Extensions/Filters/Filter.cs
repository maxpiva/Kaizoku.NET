using System;
using System.Collections.Generic;
using System.Text;
using Mihon.ExtensionsBridge.Models.Abstractions;

namespace Mihon.ExtensionsBridge.Models.Extensions.Filters
{

    public abstract class Filter<T> : IFilter
    {
        public string Name { get; }
        public T State { get; set; }

        protected Filter(string name, T state)
        {
            Name = name;
            State = state;
        }
        public object? UntypedState => State;
        
    }
}
