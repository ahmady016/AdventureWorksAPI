using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using System.Threading.Tasks;

namespace AdventureWorks
{
   public class ApiController<TEntity, TDbContext> : Controller
           where TEntity : class
           where TDbContext : DbContext
   {
      private readonly TDbContext _db;
      private readonly PropertyInfo _keyProp;
      private readonly string _tblName;
      private readonly List<string> _querable;
      private IEnumerable<dynamic> _filters;
      private string _sqlQuery;

      public ApiController( TDbContext dbContext,
                            string keyPropName,
                            string sqlTableName,
                            List<string> querableFields)
      {
         _db = dbContext;
         _keyProp = _db.Set<TEntity>().GetType().GetProperty(keyPropName);
         _tblName = sqlTableName;
         _querable = querableFields;
      }

      private bool FieldExists(string FieldName) => _querable.Any(q => q == FieldName);
      private void SetState(TEntity entity, string _state)
      {
         EntityState state;
         switch (_state) {
            case "Added":
               state = EntityState.Added;
               break;
            case "Modified":
               state = EntityState.Modified;
               break;
            case "Deleted":
               state = EntityState.Deleted;
               break;
            default:
               state = EntityState.Unchanged;
               break;
         }
         _db.Entry(entity).State = state;
      }
      private async Task<IActionResult> DoAction(TEntity entity, string actionType)
      {
         if (ModelState.IsValid) {
            SetState(entity, actionType);
            try {
               await _db.SaveChangesAsync();
               switch (actionType) {
                  case "Added":
                     return Created($"api/Products/{_keyProp.GetValue(entity)}", entity);
                  case "Modified":
                     return Ok(entity);
                  case "Deleted":
                     return Ok(new { Message = $"Item with Id: {_keyProp.GetValue(entity)} Was Deleted From DB" });
                  default:
                     return NoContent();
               }
            }
            catch (Exception ex) {
               return BadRequest(new { Title = "SqlException", Error = ex.InnerException.Message });
            }
         }
         else {
            return BadRequest(new { Title = "Invalid Data", Error = ModelState });
         }
      }

      [HttpGet("list")]
      public async Task<IActionResult> GetAll() =>
          Ok(await _db.Set<TEntity>().AsNoTracking().ToListAsync());

      [HttpGet("{id}")]
      public async Task<IActionResult> GetById([FromRoute] object id)
      {
         var item = await _db.Set<TEntity>()
                           .AsNoTracking()
                           .FirstOrDefaultAsync(e => _keyProp.GetValue(e) == id);
         if (item == null)
            return NotFound();
         return Ok(item);
      }

      [HttpGet("page")]
      public IActionResult GetPaged([FromQuery] Page page) =>
                  Ok(new PagedList<TEntity>(_db.Set<TEntity>().AsNoTracking(), page.number, page.size));

      [HttpGet("find")]
      public async Task<IActionResult> Find([FromQuery] IDictionary<string, string> query)
      {
         // capitalize the first letter of the field
         _filters = query.Select(item => new
         {
            Field = item.Key.Substring(0, 1).ToString().ToUpper() + item.Key.Substring(1),
            Value = item.Value
         });
         // filter out any not querable fields
         _filters = _filters.Where(f => f.Field == FieldExists(f.field));
         // if no remaining fields return badrequest
         if(_filters.Count() == 0)
            return BadRequest(new { Title = "Invalid QueryString Keys", Error = query });
         // set the base select statement
         _sqlQuery = $"select * from {_tblName}";
         // build the where clause
         foreach (var item in _filters)
         {
            if (item.Field == _filters.First().Field)
               _sqlQuery += $" where {item.Field} = '{item.Value}'";
            else
               _sqlQuery += $" and {item.Field} = '{item.Value}'";
         }
         // execute the query
         var result = await _db.Set<TEntity>()
                            .AsNoTracking()
                            .FromSql(_sqlQuery)
                            .ToListAsync();
         // return the appropriate result
         if (result.Count > 0)
            return Ok(result);
         else
            return NotFound();
      }

      [HttpPost("add")]
      public async Task<IActionResult> Add([FromBody] TEntity newProduct)
      {
         return await DoAction(newProduct, "Added");
      }

      [HttpPut("update")]
      public async Task<IActionResult> Update([FromBody] TEntity updProduct)
      {
         return await DoAction(updProduct, "Modified");
      }

      [HttpDelete("delete/{id}")]
      public async Task<IActionResult> Delete([FromRoute] object id)
      {
         var item = await _db.Set<TEntity>()
                           .AsNoTracking()
                           .FirstOrDefaultAsync(e => _keyProp.GetValue(e) == id);
         if (item == null)
            return NotFound();
         return await DoAction(item, "Deleted");
      }

   } // class
} // namespace