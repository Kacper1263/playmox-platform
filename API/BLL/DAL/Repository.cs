using System.Data;
using System.Linq.Expressions;
using System.Linq.Dynamic.Core;
using PlaymoxAPI.BLL.BO;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace PlaymoxAPI.BLL.DAL;

public class Repository<T, K, C> : IDisposable
     where T : BaseEntity<K>
     where K : IComparable<K>
     where C : DbContext, new()
    {
        public virtual string GetVersion()
        {
            return "0.1";
        }

        private Dictionary<System.Data.IsolationLevel, int> IsolationLevelPower = new Dictionary<System.Data.IsolationLevel, int>
        {
            { System.Data.IsolationLevel.Unspecified, 0 },
            { System.Data.IsolationLevel.Chaos, 1 },
            { System.Data.IsolationLevel.ReadUncommitted, 2 },
            { System.Data.IsolationLevel.ReadCommitted, 3 },
            { System.Data.IsolationLevel.RepeatableRead, 4 },
            { System.Data.IsolationLevel.Snapshot, 5 },
            { System.Data.IsolationLevel.Serializable, 6 },
        };

        public Repository(C dc = null)
        {
            if (dc == null)
            {
                DC = new C();
            }
            else
            {
                DC = dc;
            }
        }

        public virtual string Test()
        {
            // return DC.Database.SqlQuery<string>("select @@VERSION").FirstOrDefault();
            return "";
        }

        public C DC { get; set; }

        public virtual T Add(T o, bool withSave = true)
        {
            DC.Set<T>().Add(o);
            SaveChanges(withSave);
            return o;
        }

        public virtual async Task<T> AddAsync(T o, bool withSave = true)
        {
            DC.Set<T>().Add(o);
            await SaveChangesAsync(withSave);
            return o;
        }

        public virtual async Task<R> AddAsync<R, S>(R o, bool withSave = true) where R : BaseEntity<S>
        {
            DC.Set<R>().Add(o);
            await SaveChangesAsync(withSave);
            return o;
        }

        public virtual void AddRange(IEnumerable<T> os, bool withSave = true)
        {
            foreach (var o in os)
            {
                DC.Set<T>().Add(o);
            }
            SaveChanges(withSave);
        }

        public virtual X Add<X>(X o, bool withSave = true)
            where X : class
        {
            DC.Set<X>().Add(o);
            SaveChanges(withSave);
            return o;
        }

        public virtual void AddRange<X>(IEnumerable<X> os, bool withSave = true) where X : class
        {
            foreach (var o in os)
            {
                DC.Set<X>().Add(o);
            }
            SaveChanges(withSave);
        }

        public virtual void Delete(T o, bool withSave = true)
        {
            DC.Set<T>().Remove(o);
            SaveChanges(withSave);
        }

        public virtual void Delete(IEnumerable<T> r, bool withSave = true)
        {
            DC.Set<T>().RemoveRange(r);
            SaveChanges(withSave);
        }

        public virtual void Delete(object id, bool withSave = true)
        {
            T o = DC.Set<T>().Find(id);

            if (o != null)
            {
                DC.Set<T>().Remove(o);
                SaveChanges(withSave);
            }
        }

        private void SaveChanges(bool save)
        {
            if (save)
            {
                DC.SaveChanges();
            }
        }

        public void SaveChanges()
        {
            DC.SaveChanges();
        }

        public async Task SaveChangesAsync(bool save = true)
        {
            if (save)
            {
                await DC.SaveChangesAsync();
            }
        }

        public virtual IQueryable<T> Q
        {
            get
            {
                return DC.Set<T>().AsNoTracking();
            }
        }

        public virtual IQueryable<T> QTracking
        {
            get
            {
                return DC.Set<T>();
            }
        }

        public virtual R Get<R, S>(S id, params Expression<Func<R, object>>[] propertiesToLoad)
            where R : BaseEntity<S>
            where S : IComparable<S>
        {
            IQueryable<R> q = DC.Set<R>();
            foreach (var p in propertiesToLoad)
            {
                q = q.Include(p);
            };

            var res = q.Where("Id = @0", id).SingleOrDefault();
            return res;
        }

        public virtual T Get(K id, params Expression<Func<T, object>>[] propertiesToLoad)
        {
            IQueryable<T> q = DC.Set<T>();
            foreach (var p in propertiesToLoad)
            {
                q = q.Include(p);
            };

            var res = q.Where("Id = @0", id).SingleOrDefault();
            return res;
        }

        public virtual async Task<T> GetAsync(K id, params Expression<Func<T, object>>[] propertiesToLoad)
        {
            IQueryable<T> q = DC.Set<T>();
            foreach (var p in propertiesToLoad)
            {
                q = q.Include(p);
            };

            var res = await q.Where("Id = @0", id).SingleOrDefaultAsync();
            return res;
        }

        /// <summary>
        /// Executes an action within a database transaction. If a transaction already exists, executes within the current transaction.
        /// </summary>
        /// <param name="action"></param>
        /// <param name="minIsolationLevel"></param>
        public void ExecInTransaction(Action action, System.Data.IsolationLevel? minIsolationLevel = null)
        {
            ExecInTransaction(() => { action(); return true; }, minIsolationLevel);
        }

        /// <summary>
        /// Executes a function within a database transaction. If a transaction already exists, executes within the current transaction.
        /// </summary>
        /// <typeparam name="R"></typeparam>
        /// <param name="action"></param>
        /// <param name="minIsolationLevel"></param>
        /// <returns></returns>
        public R ExecInTransaction<R>(Func<R> action, System.Data.IsolationLevel? minIsolationLevel = null, int deadlockRetryCount = 5)
        {
            R result;
            var currentTransaction = DC.Database.CurrentTransaction;

            if (currentTransaction == null)
            {
                R rr = ExecWithDeadlockResolver(action, deadlockRetryCount, minIsolationLevel);

                return rr;
            }
            else
            {
                if (minIsolationLevel != null)
                {
                    var minTranasactionIsolationLevelPower = IsolationLevelPower[minIsolationLevel.Value];
                    //var currentTranasactionIsolationLevelPower = IsolationLevelPower[currentTransaction.UnderlyingTransaction.IsolationLevel];

                    //if (minTranasactionIsolationLevelPower > currentTranasactionIsolationLevelPower)
                    //{
                    //    throw new Exception($"[Nested transaction] Minimum required isolation level: {minIsolationLevel.Value}");
                    //}
                }

                result = action();
            }
            return result;
        }

        private R ExecWithDeadlockResolver<R>(Func<R> action, int deadlockRetryCount, System.Data.IsolationLevel? minIsolationLevel)
        {
            SqlException deadlockException = null;

            for (int i = 0; i < deadlockRetryCount; i++)
            {
                try
                {
                    var isolationLevel = minIsolationLevel ?? IsolationLevel.ReadCommitted;

                    using (var tr = DC.Database.BeginTransaction(isolationLevel))
                    {
                        var result = action();
                        tr.Commit();
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    var sqlex = ex.InnerException as SqlException;

                    if (sqlex == null)
                    {
                        sqlex = ex.InnerException?.InnerException as SqlException;
                    }

                    if (sqlex?.Number == 1205) // deadlock
                    {
                        deadlockException = sqlex;
                        Thread.Sleep(new Random().Next(250, 500));
                        continue;
                    }

                    throw;
                }
            }

            throw new Exception("Unresolved database deadlock.", deadlockException);
        }

        public virtual T AddOrUpdate(T obj, bool withSave = true)
        {
            var objDb = DC.Set<T>().Find(obj.Id);

            if (objDb == null)
            {
                objDb = DC.Add(obj).Entity;
            }
            else
            {
                DC.Entry(objDb).CurrentValues.SetValues(obj);
                DC.Entry(objDb).State = EntityState.Modified;
            }

            SaveChanges(withSave);

            return objDb;
        }

        /// <summary>
        /// Usage: AddOrUpdate&lt;BO.Client, long&gt;(client, true)
        /// </summary>
        /// <typeparam name="R"></typeparam>
        /// <typeparam name="S"></typeparam>
        /// <param name="obj"></param>
        /// <param name="withSave"></param>
        public virtual void AddOrUpdate<R, S>(R obj, bool withSave = true) where R : BaseEntity<S>
        {
            var objDb = DC.Set<R>().Find(obj.Id);

            if (objDb == null)
            {
                DC.Add(obj);
            }
            else
            {
                DC.Entry(objDb).CurrentValues.SetValues(obj);
                DC.Entry(objDb).State = EntityState.Modified;
            }

            SaveChanges(withSave);
        }

        public virtual void Update(T obj, bool withSave = true)
        {
            var objDb = DC.Set<T>().Find(obj.Id);
            DC.Entry(objDb).CurrentValues.SetValues(obj);
            DC.Entry(objDb).State = EntityState.Modified;
            SaveChanges(withSave);
        }

        public async virtual Task<T> UpdateAsync(T obj, bool withSave = true)
        {
            var objDb = DC.Set<T>().Find(obj.Id);
            DC.Entry(objDb).CurrentValues.SetValues(obj);
            DC.Entry(objDb).State = EntityState.Modified;
            await SaveChangesAsync(withSave);

            return objDb;
        }

        public virtual void Update<R, S>(R obj, bool withSave = true) where R : BaseEntity<S>
        {
            var objDb = DC.Set<R>().Find(obj.Id);
            DC.Entry(objDb).CurrentValues.SetValues(obj);
            DC.Entry(objDb).State = EntityState.Modified;
            SaveChanges(withSave);
        }

        public int SqlRaw(FormattableString q)
        {
            return DC.Database.ExecuteSqlInterpolated(q);
        }

        public async Task<int> SqlRawAsync(FormattableString q)
        {
            return await DC.Database.ExecuteSqlInterpolatedAsync(q);
        }

        public virtual void Dispose()
        {
            if (DC != null)
            {
                DC.Dispose();
            }
        }
    }
