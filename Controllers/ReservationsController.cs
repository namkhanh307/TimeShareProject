﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using ECommerceMVC.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using TimeShareProject.Models;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using Transaction = TimeShareProject.Models.Transaction;

namespace TimeShareProject.Controllers
{

    //public class PaypalOrderRequest
    //{
    //    public decimal Amount { get; set; }
    //    public string Type { get; set; }
    //    public string TransactionId { get; set; }
    //}

    public class ReservationsController : Controller
    {
        private readonly _4restContext _context;
        private readonly PaypalClient _paypalClient;

        public ReservationsController(_4restContext context, PaypalClient paypalClient)
        {
            _context = context;
            _paypalClient = paypalClient;
        }

        public PartialViewResult FilterDuplicate()
        {
            var reservations = _context.Reservations.Include(r => r.Block).Include(r => r.Property).Include(r => r.User).ToList();

            var duplicateReservations = reservations
                .Where(r => r.Type == 1) // Filter only reservations with type "Reserve"
                .GroupBy(r => new { r.PropertyId, r.BlockId }) // Group by property ID and block ID
                .Where(g => g.Count() > 1) // Filter groups with more than one reservation
                .SelectMany(g => g); // Flatten the groups back into individual reservations

            return PartialView("_FilteredReservations", duplicateReservations);
        }
        #region Paypal payment


        [Authorize]
        [HttpPost("/Reservations/create-paypal-order")]
        public async Task<IActionResult> CreatePaypalOrder([FromBody] Transaction transaction, CancellationToken cancellationToken)
        {
            double total = Math.Round((double)transaction.Amount / 23000, 2);
            var totalString = total.ToString();
            var currency = "USD";
            var transactionCode = Common.GetTransactionCode(transaction.Id);
            try
            {
                var response = await _paypalClient.CreateOrder(totalString, currency, transactionCode);
                if (response != null)
                {
                    var newTrasaction = await _context.Transactions.FindAsync(transaction.Id);
                    try
                    {
                        newTrasaction.Status = true;
                        newTrasaction.TransactionCode = transactionCode;
                        _context.Update(newTrasaction);
                        await _context.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        ModelState.AddModelError("", "Error occurred while updating the transaction.");
                    }
                }
                return Ok(response);
            }
            catch (Exception ex)
            {
                var error = new { ex.GetBaseException().Message };
                return BadRequest(error);
            }
        }

        [Authorize]
        [HttpPost("/Reservations/capture-paypal-order")]

        public async Task<IActionResult> CapturePaypalOrder(string orderID, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _paypalClient.CaptureOrder(orderID);
                return Ok(response);
            }
            catch (Exception ex)
            {
                var error = new { ex.GetBaseException().Message };
                return BadRequest(error);
            }
        }
        #endregion

       
        // GET: Reservations
        [Authorize(Roles = "1,2")]
        public async Task<IActionResult> Index()
        {
            List<int> distinctReservation = Common.GetDistinctReservation();
            ViewBag.DistinctReservation = distinctReservation;
            var timeShareProjectContext = _context.Reservations.Include(r => r.Block).Include(r => r.Property).Include(r => r.User);
            return View(await timeShareProjectContext.ToListAsync());
        }
        // GET: Reservations/Details/5

        public async Task<IActionResult> TransactionDetail(int id)
        {
            var timeShareProjectContext = _context.Transactions.Include(t => t.Reservation).Where(m => m.ReservationId == id);
            ViewBag.count = timeShareProjectContext.Count();
            return View(await timeShareProjectContext.ToListAsync());
        }
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var reservation = await _context.Reservations
                .Include(r => r.Block)
                .Include(r => r.Property)
                .Include(r => r.User)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (reservation == null)
            {
                return NotFound();
            }

            return View(reservation);
        }

        public IActionResult SelectRoom(int blockSelect, int propertyId, string saleStatus, int projectId, int bedSelect)
        {
            if (!User.Identity.IsAuthenticated)
            {
                var returnUrl = Url.Action("SelectRoom", "Reservations", new { blockSelect, propertyId, saleStatus, projectId, bedSelect });
                return RedirectToAction("Login", "Login", new { returnUrl });
            }

            var block = _context.Blocks.FirstOrDefault(b => b.Id == blockSelect);
            var property = _context.Properties.FirstOrDefault(p => p.Id == propertyId);

            if (block == null || property == null)
            {
                return RedirectToAction("GetProperty", "Properties", new { property.Id });
            }
            ViewBag.SaleStatus = saleStatus;
            ViewBag.Block = block;
            ViewBag.Property = property;
            ViewBag.ProjectId = projectId;
            ViewBag.BedSelect = bedSelect;
            return View();
        }

        public IActionResult ConfirmReservation(int propertyId, int blockSelect, string transactionCode, string saleStatus)
        {
            string username = User.Identity.Name;
            var user = _context.Users.FirstOrDefault(u => u.Account.Username == username);
            var property = _context.Properties.FirstOrDefault(p => p.Id == propertyId);


            int reservationType = 2;
            int transactionType = 0;
            if (saleStatus == "Reserve")
            {
                reservationType = 1;
                transactionType = -1;
            }
            try
            {
                using (var transaction = _context.Database.BeginTransaction())
                {
                    var newReservation = new Reservation()
                    {
                        PropertyId = propertyId,
                        BlockId = blockSelect,
                        UserId = user.Id,
                        YearQuantity = 10,
                        RegisterDate = DateTime.Now,
                        Type = reservationType,
                    };

                    _context.Reservations.Add(newReservation);
                    _context.SaveChanges();

                    if (reservationType == 1)
                    {
                        var newReserveTransaction = new Transaction()
                        {
                            Date = DateTime.Now,
                            Amount = 500000,
                            Status = false,
                            TransactionCode = transactionCode,
                            ReservationId = newReservation.Id,
                            Type = transactionType,
                            DeadlineDate = Common.GetSaleDate(propertyId)

                        };
                        _context.Transactions.Add(newReserveTransaction);
                        _context.SaveChanges();
                    }

                    if (transactionType == 0)
                    {

                        var newDepositTransaction = new Transaction()
                        {
                            Date = DateTime.Now,
                            Amount = property.UnitPrice,
                            Status = false,
                            TransactionCode = transactionCode,
                            ReservationId = newReservation.Id,
                            Type = transactionType,
                            DeadlineDate = Common.GetSaleDate(propertyId).AddDays(1)

                        };
                        _context.Transactions.Add(newDepositTransaction);

                        var newFirstTermTransaction = new Transaction()
                        {
                            Date = DateTime.Now,
                            Amount = property.UnitPrice * 3,
                            Status = false,
                            TransactionCode = null,
                            ReservationId = newReservation.Id,
                            Type = 1,
                            DeadlineDate = Common.GetSaleDate(propertyId).AddDays(7)


                        };
                        _context.Transactions.Add(newFirstTermTransaction);

                        var newSecondTermTransaction = new Transaction()
                        {
                            Date = DateTime.Now,
                            Amount = property.UnitPrice * 3,
                            Status = false,
                            TransactionCode = null,
                            ReservationId = newReservation.Id,
                            Type = 2,
                            DeadlineDate = Common.GetSaleDate(propertyId).AddDays(365)

                        };
                        _context.Transactions.Add(newSecondTermTransaction);


                        var newThirdTermTransaction = new Transaction()
                        {
                            Date = DateTime.Now,
                            Amount = property.UnitPrice * 3,
                            Status = false,
                            TransactionCode = null,
                            ReservationId = newReservation.Id,
                            Type = 3,
                            DeadlineDate = Common.GetSaleDate(propertyId).AddDays(730)

                        };
                        _context.Transactions.Add(newThirdTermTransaction);
                        _context.SaveChanges();
                    }
                    transaction.Commit();
                    TempData["Message"] = "Reservation confirmed successfully!";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "An error occurred while confirming the reservation.";
            }
            return RedirectToAction("Index", "Home");
        }


        // GET: Reservations/Create
        public IActionResult Create()
        {
            ViewData["BlockId"] = new SelectList(_context.Blocks, "Id", "Id");
            ViewData["PropertyId"] = new SelectList(_context.Properties, "Id", "Id");
            ViewData["UserId"] = new SelectList(_context.Users, "Id", "Id");
            return View();
        }

        // POST: Reservations/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,PropertyId,UserId,RegisterDate,YearQuantity,Type,BlockId")] Reservation reservation)
        {
            if (ModelState.IsValid)
            {
                _context.Add(reservation);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["BlockId"] = new SelectList(_context.Blocks, "Id", "Id", reservation.BlockId);
            ViewData["PropertyId"] = new SelectList(_context.Properties, "Id", "Id", reservation.PropertyId);
            ViewData["UserId"] = new SelectList(_context.Users, "Id", "Id", reservation.UserId);
            return View(reservation);
        }

        // GET: Reservations/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var reservation = await _context.Reservations.FindAsync(id);
            if (reservation == null)
            {
                return NotFound();
            }
            ViewData["BlockId"] = new SelectList(_context.Blocks, "Id", "Id", reservation.BlockId);
            ViewData["PropertyId"] = new SelectList(_context.Properties, "Id", "Id", reservation.PropertyId);
            ViewData["UserId"] = new SelectList(_context.Users, "Id", "Id", reservation.UserId);
            return View(reservation);
        }

        // POST: Reservations/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,PropertyId,UserId,RegisterDate,YearQuantity,Type,BlockId")] Reservation reservation)
        {
            if (id != reservation.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(reservation);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ReservationExists(reservation.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["BlockId"] = new SelectList(_context.Blocks, "Id", "Id", reservation.BlockId);
            ViewData["PropertyId"] = new SelectList(_context.Properties, "Id", "Id", reservation.PropertyId);
            ViewData["UserId"] = new SelectList(_context.Users, "Id", "Id", reservation.UserId);
            return View(reservation);
        }

        // GET: Reservations/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var reservation = await _context.Reservations
                .Include(r => r.Block)
                .Include(r => r.Property)
                .Include(r => r.User)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (reservation == null)
            {
                return NotFound();
            }

            return View(reservation);
        }

        // POST: Reservations/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var reservation = await _context.Reservations.FindAsync(id);
            if (reservation != null)
            {
                _context.Reservations.Remove(reservation);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool ReservationExists(int id)
        {
            return _context.Reservations.Any(e => e.Id == id);
        }
        public PartialViewResult FilterReservation(int? propertyId, int? blockId, int? type)
        {
            using (_4restContext context = new _4restContext())
            {
                var query = context.Reservations.AsQueryable();
                query = query.Include(r => r.Block).Include(r => r.Property).Include(r => r.User);

                if (propertyId != null)
                {
                    query = query.Where(r => r.PropertyId == propertyId);
                }

                if (blockId != null)
                {
                    query = query.Where(r => r.BlockId == blockId);
                }

                if (type != null)
                {
                    query = query.Where(r => r.Type == type);
                }

                var filteredReservations = query.ToList();

                return PartialView("_FilteredReservations", filteredReservations);
            }
        }
        public PartialViewResult Search(string searchTerm)
        {
            // Filter reservations based on search term
            var filteredReservations = _context.Reservations.Include(r => r.Block).Include(r => r.User).Include(r => r.Property)
                .Where(r => r.Property.Name.Contains(searchTerm) || r.User.Name.Contains(searchTerm))
                .ToList();

            return PartialView("_FilteredReservations", filteredReservations);
        }
    }
}
