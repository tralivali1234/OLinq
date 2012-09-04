﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Linq.Expressions;

namespace OLinq
{

    class SelectOperation<TSource, TResult> : Operation<IEnumerable<TResult>>, IEnumerable<TResult>, INotifyCollectionChanged
    {

        Expression sourceExpr;
        Expression<Func<TSource, TResult>> lambdaExpr;
        IOperation<IEnumerable<TSource>> source;
        Dictionary<TSource, LambdaOperation<TResult>> funcs = new Dictionary<TSource, LambdaOperation<TResult>>();

        public SelectOperation(OperationContext context, MethodCallExpression expression)
            : base(context, expression)
        {
            sourceExpr = expression.Arguments[0];
            lambdaExpr = expression.Arguments[1] as Expression<Func<TSource, TResult>>;

            // attempt to unpack from unary
            if (lambdaExpr == null)
            {
                var unaryExpr = expression.Arguments[1] as UnaryExpression;
                if (unaryExpr != null)
                    lambdaExpr = unaryExpr.Operand as Expression<Func<TSource, TResult>>;
            }

            source = OperationFactory.FromExpression<IEnumerable<TSource>>(context, sourceExpr);
            source.ValueChanged += source_ValueChanged;
        }

        void source_ValueChanged(object sender, ValueChangedEventArgs args)
        {
            var oldValue = args.OldValue as INotifyCollectionChanged;
            if (oldValue != null)
                oldValue.CollectionChanged -= source_CollectionChanged;

            var newValue = args.NewValue as INotifyCollectionChanged;
            if (newValue != null)
                newValue.CollectionChanged += source_CollectionChanged;

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset, null));
        }

        void source_CollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            switch (args.Action)
            {
                case NotifyCollectionChangedAction.Reset:
                case NotifyCollectionChangedAction.Move:
                case NotifyCollectionChangedAction.Replace:
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset, null));
                    break;
                case NotifyCollectionChangedAction.Add:
                    var newItems = args.NewItems.Cast<TSource>().Select(i => GetFuncResult(i)).ToList();
                    if (newItems.Count > 0)
                        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, newItems, args.NewStartingIndex));
                    break;
                case NotifyCollectionChangedAction.Remove:
                    var oldItems = args.OldItems.Cast<TSource>().Select(i => GetFuncResult(i)).ToList();
                    if (oldItems.Count > 0)
                        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, oldItems, args.OldStartingIndex));
                    break;
            }
        }

        /// <summary>
        /// Invoked when any of the current tests change.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        void func_ValueChanged(object sender, ValueChangedEventArgs args)
        {
            var oldValue = (TResult)args.OldValue;
            var newValue = (TResult)args.NewValue;

            // store new test result
            if (!object.Equals(oldValue, newValue))
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, new TResult[] { newValue }, new TResult[] { oldValue }));
        }

        private LambdaOperation<TResult> GetFunc(TSource item)
        {
            LambdaOperation<TResult> func;
            if (!funcs.TryGetValue(item, out func))
            {
                // generate new parameter
                var ctx = new OperationContext(Context);
                var var = new ValueOperation<TSource>(item);
                var.Load();
                ctx.Variables[lambdaExpr.Parameters[0].Name] = var;

                // create new test and subscribe to test modifications
                func = new LambdaOperation<TResult>(ctx, lambdaExpr);
                func.Tag = item;
                func.Load(); // load before value changed to prevent double notification
                func.ValueChanged += func_ValueChanged;
                funcs[item] = func;
            }

            return func;
        }

        private TResult GetFuncResult(TSource item)
        {
            return GetFunc(item).Value;
        }

        IEnumerator<TResult> GetEnumerator()
        {
            return source.Value.Select(i => GetFuncResult(i)).GetEnumerator();
        }

        public override void Load()
        {
            if (source != null)
                source.Load();
            base.Load();

            SetValue(this);
        }

        IEnumerator<TResult> IEnumerable<TResult>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public event NotifyCollectionChangedEventHandler CollectionChanged;

        private void OnCollectionChanged(NotifyCollectionChangedEventArgs args)
        {
            if (CollectionChanged != null)
                CollectionChanged(this, args);
        }

    }

}
